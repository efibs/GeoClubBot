using System.Text.Json;
using Entities;
using Infrastructure.OutputAdapters.GeoGuessr;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Review;
using UseCases.UseCases.AI.StreetView;

namespace GeoClubBot.RetrievalProbe;

/// <summary>
/// <c>/ai review</c> run for real, for any player: the bot's own <see cref="ReviewRoundsHandler"/> places
/// the rounds and guesses with Natural Earth's outlines, searches the production index per round, reads
/// each round's views from Street View and has a free vision model check them against the clues, then
/// asks the bot's models once. What it reviews is one of:
/// <list type="bullet">
/// <item>a challenge's results as GeoGuessr returned them (the ApiProbe's <c>raw /v3/results/highscores/&lt;token&gt;</c>
/// output, saved to a file), standing in for the member's linked account with the player named by <c>--player</c>;</item>
/// <item>one game (the ApiProbe's <c>raw /v3/games/&lt;token&gt;</c> output, saved to a file);</item>
/// <item>a Google Maps Street View link, long or short, followed live.</item>
/// </list>
/// It reads GeoGuessr only from the file, so it needs no token. Costs what the command costs: the
/// embedding request, one vision request per round and one chat request or more.
/// </summary>
public static class ReviewCommand
{
    public static async Task RunAsync(ProbeContext context)
    {
        var arguments = context.Arguments;
        if (!arguments.AllowChat)
        {
            throw new ProbeException("review asks the bot's models (an embedding, a vision request per round and a chat request or more), so it has to be allowed by name: pass --allow-chat.");
        }

        if (context.Settings.ApiKey is not { } apiKey)
        {
            throw new ProbeException(ProbeSettings.MissingKeyHelp);
        }

        var target = arguments.Target;
        if (target is not { Length: > 0 })
        {
            throw new ProbeException(
                "review needs a file with a challenge's highscores or a game (the ApiProbe's output of raw \"/v3/results/highscores/<token>?friends=false&limit=26&minRounds=1\" "
                + "or raw \"/v3/games/<token>\"), or a Google Maps Street View link.");
        }

        var root = ProbeSettings.RepositoryRoot() ?? throw new ProbeException("GeoClubBot.sln was not found above the probe.");
        var settings = BotSettings.Load(BotSettings.DefaultPath(root), arguments.Overrides ?? [], apiKey);

        // The committed settings ship the assistant switched off; the probe is asked for a review by name.
        settings.Ai.Active = true;
        using var stack = AnsweringStack.Create(context, settings);
        var refreshed = await stack.Catalog.RefreshAsync(context.CancellationToken).ConfigureAwait(false);
        if (refreshed.IsFailure)
        {
            throw new ProbeException($"The free model roster could not be read: {refreshed.Error.Message}");
        }

        const ulong member = 1;
        string link;
        string heading;
        ChallengeResultPlayerDto? player = null;
        var clientAnswers = new Dictionary<string, Func<object?[], object?>>();

        if (StreetViewLink.Find(target) is { } maps)
        {
            link = maps;
            heading = "# Street View place";
        }
        else if (File.Exists(target))
        {
            var text = await File.ReadAllTextAsync(target, context.CancellationToken).ConfigureAwait(false);
            if (ReadGame(text) is { } game)
            {
                player = game.Player;
                link = $"https://www.geoguessr.com/game/{game.Token ?? "GameFromFile"}";
                clientAnswers[nameof(IGeoGuessrClient.ReadGameAsync)] = _ => Task.FromResult<ChallengeResultGameDto?>(game);
                heading = $"# Review of {player.Nick}'s game on {game.MapName ?? "an unknown map"} ({player.TotalScoreInPoints:N0} points)";
            }
            else
            {
                var highscores = Read(text);
                var players = highscores.Items.Select(item => item.Game.Player).ToList();
                player = arguments.Question is { Length: > 0 } wanted
                    ? players.FirstOrDefault(p => string.Equals(p.Nick, wanted, StringComparison.OrdinalIgnoreCase) || p.Id == wanted)
                      ?? throw new ProbeException($"No player '{wanted}' in the results; they hold: {string.Join(", ", players.Select(p => p.Nick))}.")
                    : players[0];
                // The results come from the file whatever the token; it only has to read as a challenge's.
                link = "https://www.geoguessr.com/challenge/ResultsFromFile";
                clientAnswers[nameof(IGeoGuessrClient.ReadHighscoresAsync)] = _ => Task.FromResult(highscores);
                heading = $"# Round review of {player.Nick} ({player.TotalScoreInPoints:N0} points)";
            }
        }
        else
        {
            throw new ProbeException($"'{target}' is neither a file nor a Google Maps link.");
        }

        var linked = player;
        var users = ProbeFakes.Answering<IGeoGuessrUserRepository>(new Dictionary<string, Func<object?[], object?>>
        {
            [nameof(IGeoGuessrUserRepository.ReadUserByDiscordUserIdAsync)] = _ => Task.FromResult<GeoGuessrUser?>(
                linked is null ? null : GeoGuessrUser.Create(linked.Id, linked.Nick, member))
        });
        var client = ProbeFakes.Answering<IGeoGuessrClient>(clientAnswers);
        var clients = ProbeFakes.Answering<IGeoGuessrClientFactory>(new Dictionary<string, Func<object?[], object?>>
        {
            [nameof(IGeoGuessrClientFactory.CreateClient)] = _ => client
        });

        var budget = new InMemoryBudgetRepository();
        var handler = stack.CreateReviewHandler(users, clients, budget, settings.RoundReview);

        var started = DateTimeOffset.UtcNow;
        var result = await handler.Handle(new ReviewRoundsCommand(member, link), context.CancellationToken).ConfigureAwait(false);

        var report = context.Report;
        report.Line(heading);
        report.Line();
        if (result.IsFailure)
        {
            report.Line($"Failed: {result.Error.Code} — {result.Error.Message}");
            Console.Write(report.ToString());
            return;
        }

        var review = result.Value;
        report.Line("| # | Location | Guess | km | Points | Checked | Seen | To look for |");
        report.Line("|---|---|---|---|---|---|---|---|");
        foreach (var round in review.Rounds)
        {
            var evidence = round.Evidence;
            report.Line($"| {round.Number} | {round.CountryCode} {round.Region} | {round.GuessCountryCode} {round.GuessRegion}{(round.MissedCountry ? " ✗" : string.Empty)} "
                        + $"| {round.DistanceKm:N0} | {round.Points:N0} | {(evidence?.Checked == true ? "yes" : "no")} "
                        + $"| {string.Join(" ", evidence?.Seen.Select(seen => $"[{seen.Marker}]{(seen.Regional ? "r" : string.Empty)}") ?? [])} "
                        + $"| {string.Join(" ", evidence?.ToLookFor.Select(marker => $"[{marker}]") ?? [])} |");
        }

        report.Line();
        foreach (var round in review.Rounds.Where(round => round.Evidence is { Seen.Count: > 0 } or { AlsoSeen.Count: > 0 }))
        {
            report.Line($"- Round {round.Number}: seen {string.Join("; ", round.Evidence!.Seen.Select(seen => $"[{seen.Marker}] {seen.Where}"))}"
                        + (round.Evidence.AlsoSeen.Count > 0 ? $" · also {string.Join("; ", round.Evidence.AlsoSeen)}" : string.Empty));
        }

        report.Line();
        report.Line($"Answered by {review.Answer.ModelUsed} in {(DateTimeOffset.UtcNow - started).TotalSeconds:N0} s; "
                    + $"{stack.RequestsSent} request(s) sent, {budget.Claimed} claimed from the allowance.");
        report.Line();
        report.Line(review.Answer.Text);
        report.Line();
        foreach (var source in review.Answer.Sources)
        {
            report.Line($"[{source.Marker}] {source.Label} — {source.Url}");
        }

        foreach (var image in review.Answer.Images.Where(image => stack.RoundViews.PathOf(image.ImageUrl) is not null))
        {
            report.Line($"-# [{image.Marker}] {image.Title}: {stack.RoundViews.PathOf(image.ImageUrl)}");
        }

        foreach (var notice in review.Answer.Notices)
        {
            report.Line($"-# {notice}");
        }

        Console.Write(report.ToString());
    }

    /// <summary>The ApiProbe's output (a request line, then the JSON, then its census) or the JSON alone.</summary>
    public static ChallengeResultHighscoresDto Read(string text) =>
        Deserialize<ChallengeResultHighscoresDto>(text) ?? throw new ProbeException("The file's JSON holds no results.");

    /// <summary>One game, as <c>/v3/games/{token}</c> returns it; null when the JSON is a challenge's results instead.</summary>
    public static ChallengeResultGameDto? ReadGame(string text)
    {
        var json = Json(text);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("items", out _) || !document.RootElement.TryGetProperty("rounds", out _)
            ? null
            : Deserialize<ChallengeResultGameDto>(text);
    }

    private static T? Deserialize<T>(string text)
    {
        var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(Json(text)));
        return JsonSerializer.Deserialize<T>(ref reader, GeoGuessrJsonContext.Default.Options);
    }

    /// <summary>The first JSON value in the text: the ApiProbe writes a request line before it and a census after.</summary>
    private static string Json(string text)
    {
        var start = text.IndexOf('{', StringComparison.Ordinal);
        if (start < 0)
        {
            throw new ProbeException("The file holds no JSON.");
        }

        var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(text[start..]));
        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.GetRawText();
    }
}
