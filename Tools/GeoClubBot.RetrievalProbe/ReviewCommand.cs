using System.Text.Json;
using Entities;
using Refit;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Review;

namespace GeoClubBot.RetrievalProbe;

/// <summary>
/// <c>/ai review</c> run for real, but for any player: a challenge's results as GeoGuessr returned them
/// (the ApiProbe's <c>raw /v3/results/highscores/&lt;token&gt;</c> output, saved to a file) are handed to the
/// bot's own <see cref="ReviewRoundsHandler"/>, which places the rounds and guesses with Natural Earth's
/// outlines, searches the production index per round and asks the bot's models once.
///
/// It reads the results from the file, never from GeoGuessr, so it needs no token; and stands in for the
/// member's linked account with the player named by <c>--player</c>. Costs what the command costs: one
/// embedding request and one or more chat requests.
/// </summary>
public static class ReviewCommand
{
    public static async Task RunAsync(ProbeContext context)
    {
        var arguments = context.Arguments;
        if (!arguments.AllowChat)
        {
            throw new ProbeException("review asks the bot's models (one embedding and a chat request or more), so it has to be allowed by name: pass --allow-chat.");
        }

        if (context.Settings.ApiKey is not { } apiKey)
        {
            throw new ProbeException(ProbeSettings.MissingKeyHelp);
        }

        if (arguments.Target is not { Length: > 0 } path || !File.Exists(path))
        {
            throw new ProbeException("review needs a file with a challenge's highscores, e.g. the ApiProbe's output of: raw \"/v3/results/highscores/<token>?friends=false&limit=26&minRounds=1\"");
        }

        var highscores = Read(await File.ReadAllTextAsync(path, context.CancellationToken).ConfigureAwait(false));
        var players = highscores.Items.Select(item => item.Game.Player).ToList();
        var player = arguments.Question is { Length: > 0 } wanted
            ? players.FirstOrDefault(p => string.Equals(p.Nick, wanted, StringComparison.OrdinalIgnoreCase) || p.Id == wanted)
              ?? throw new ProbeException($"No player '{wanted}' in the results; they hold: {string.Join(", ", players.Select(p => p.Nick))}.")
            : players[0];

        var root = ProbeSettings.RepositoryRoot() ?? throw new ProbeException("GeoClubBot.sln was not found above the probe.");
        var settings = BotSettings.Load(BotSettings.DefaultPath(root), arguments.Overrides ?? [], apiKey);
        using var stack = AnsweringStack.Create(context, settings);
        var refreshed = await stack.Catalog.RefreshAsync(context.CancellationToken).ConfigureAwait(false);
        if (refreshed.IsFailure)
        {
            throw new ProbeException($"The free model roster could not be read: {refreshed.Error.Message}");
        }

        const ulong member = 1;
        var users = ProbeFakes.Answering<IGeoGuessrUserRepository>(new Dictionary<string, Func<object?[], object?>>
        {
            [nameof(IGeoGuessrUserRepository.ReadUserByDiscordUserIdAsync)] = _ => Task.FromResult<GeoGuessrUser?>(GeoGuessrUser.Create(player.Id, player.Nick, member))
        });
        var client = ProbeFakes.Answering<IGeoGuessrClient>(new Dictionary<string, Func<object?[], object?>>
        {
            [nameof(IGeoGuessrClient.ReadHighscoresAsync)] = _ => Task.FromResult(highscores)
        });
        var clients = ProbeFakes.Answering<IGeoGuessrClientFactory>(new Dictionary<string, Func<object?[], object?>>
        {
            [nameof(IGeoGuessrClientFactory.CreateClient)] = _ => client
        });

        var budget = new InMemoryBudgetRepository();
        var handler = stack.CreateReviewHandler(users, clients, budget, settings.RoundReview);
        // The results come from the file whatever the token; it only has to read as one.
        const string token = "ResultsFromFile";
        var started = DateTimeOffset.UtcNow;
        var result = await handler.Handle(new ReviewRoundsCommand(member, token), context.CancellationToken).ConfigureAwait(false);

        var report = context.Report;
        report.Line($"# Round review of {player.Nick} ({player.TotalScoreInPoints:N0} points)");
        report.Line();
        if (result.IsFailure)
        {
            report.Line($"Failed: {result.Error.Code} — {result.Error.Message}");
            Console.Write(report.ToString());
            return;
        }

        var review = result.Value;
        report.Line("| # | Location | Guess | km | Points |");
        report.Line("|---|---|---|---|---|");
        foreach (var round in review.Rounds)
        {
            report.Line($"| {round.Number} | {round.CountryCode} {round.Region} | {round.GuessCountryCode} {round.GuessRegion}{(round.MissedCountry ? " ✗" : string.Empty)} | {round.DistanceKm:N0} | {round.Points:N0} |");
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

        foreach (var notice in review.Answer.Notices)
        {
            report.Line($"-# {notice}");
        }

        Console.Write(report.ToString());
    }

    /// <summary>The ApiProbe's output (a request line, then the JSON, then its census) or the JSON alone.</summary>
    public static ChallengeResultHighscoresDto Read(string text)
    {
        var start = text.IndexOf('{', StringComparison.Ordinal);
        if (start < 0)
        {
            throw new ProbeException("The file holds no JSON.");
        }

        var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(text[start..]));
        return JsonSerializer.Deserialize<ChallengeResultHighscoresDto>(ref reader, SystemTextJsonContentSerializer.GetDefaultJsonSerializerOptions())
               ?? throw new ProbeException("The file's JSON holds no results.");
    }
}
