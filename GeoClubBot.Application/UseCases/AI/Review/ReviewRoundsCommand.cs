using Configuration;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Conversations;
using UseCases.UseCases.AI.Retrieval;
using UseCases.UseCases.AI.Tools;
using Utilities;

namespace UseCases.UseCases.AI.Review;

/// <summary>
/// Reviews a member's played challenge: which clues from the guides point to each round's location,
/// and what to study for the countries they missed.
/// </summary>
/// <param name="DiscordUserId">The member asking; their linked GeoGuessr account is the player reviewed.</param>
/// <param name="Challenge">A challenge link or token; null reviews the club's latest daily challenge the member played.</param>
public sealed record ReviewRoundsCommand(ulong DiscordUserId, string? Challenge) : ICommand<Result<RoundReview>>;

/// <param name="Answer">The review, shaped like an answer: text, pictures, sources.</param>
/// <param name="ChallengeToken">The challenge reviewed.</param>
/// <param name="Rounds">The rounds as the review read them.</param>
public sealed record RoundReview(AiAnswer Answer, string ChallengeToken, IReadOnlyList<ReviewedRound> Rounds)
{
    public int Missed => Rounds.Count(round => round.MissedCountry);
}

public static class RoundReviewErrorCodes
{
    public const string Disabled = "ai.review_disabled";
    public const string NotLinked = "ai.review_not_linked";
    public const string NoChallenge = "ai.review_no_challenge";
    public const string NotPlayed = "ai.review_not_played";
    public const string NoRounds = "ai.review_no_rounds";
    public const string ResultsUnavailable = "ai.review_results_unavailable";
}

/// <summary>
/// Reads the challenge's highscores with the main club's account (as the country challenges are read),
/// finds the member's game, places each round and guess in a country and region (GeoGuessr's own
/// country code where it gives one, the Natural Earth boundaries otherwise), searches the guides for
/// each round's place — every round's wording in one embedding request — and asks the model once, with
/// the excerpts, through the same answer loop a question goes through. Two requests of the day's
/// allowance, plus the tool rounds the model takes.
/// </summary>
public sealed partial class ReviewRoundsHandler(
    IGeoGuessrUserRepository users,
    IClubChallengeRepository challenges,
    IGeoGuessrClientFactory geoGuessrClientFactory,
    IRegionBoundaries boundaries,
    IEmbedder embedder,
    IKnowledgeIndex knowledgeIndex,
    KnowledgeCountryDirectory countryDirectory,
    AiAnswerLoop answerLoop,
    IAiBudgetRepository budget,
    IOptions<GeoGuessrConfiguration> geoGuessrConfiguration,
    IOptions<AiConfiguration> aiConfiguration,
    IOptions<AiRoundReviewConfiguration> configuration,
    TimeProvider timeProvider,
    ILogger<ReviewRoundsHandler> logger)
    : IRequestHandler<ReviewRoundsCommand, Result<RoundReview>>
{
    /// <summary>The embedding of the rounds' searches, and the chat completion: claimed up front, as a question's are.</summary>
    private const int RequestsPerReview = 2;

    public async Task<Result<RoundReview>> Handle(ReviewRoundsCommand request, CancellationToken cancellationToken)
    {
        var settings = configuration.Value;
        var ai = aiConfiguration.Value;
        if (!ai.Active || !settings.Enabled)
        {
            return Error.Validation(RoundReviewErrorCodes.Disabled, "The round review is turned off.");
        }

        var user = await users.ReadUserByDiscordUserIdAsync(request.DiscordUserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Error.Validation(RoundReviewErrorCodes.NotLinked, "Link your GeoGuessr account first (`/link`), so I know which game is yours.");
        }

        var tokens = await ResolveTokensAsync(request.Challenge, cancellationToken).ConfigureAwait(false);
        if (tokens.IsFailure)
        {
            return tokens.Error;
        }

        var client = geoGuessrClientFactory.CreateClient(geoGuessrConfiguration.Value.MainClub.ClubId);
        var game = await FindGameAsync(client, tokens.Value, user.UserId, settings, cancellationToken).ConfigureAwait(false);
        if (game.IsFailure)
        {
            return game.Error;
        }

        var (token, played) = game.Value;
        var rounds = await ReadRoundsAsync(played, settings.MaxRounds, cancellationToken).ConfigureAwait(false);
        if (rounds.Count == 0)
        {
            return Error.NotFound(RoundReviewErrorCodes.NoRounds,
                "GeoGuessr's results list carried no rounds for this game, so there is nothing to review.");
        }

        var now = timeProvider.GetUtcNow();
        var day = DateOnly.FromDateTime(now.UtcDateTime);
        if (!await budget.TryReserveRequestsAsync(day, RequestsPerReview, ai.OpenRouter.DailyRequestBudget, cancellationToken).ConfigureAwait(false))
        {
            return AiAnswerErrors.BudgetExhausted;
        }

        var hits = await RetrieveAsync(rounds, settings, cancellationToken).ConfigureAwait(false);
        var question = RoundReviewPrompt.Build(rounds, token);

        var completion = await answerLoop.AnswerAsync(
                new AnswerRequest(ConversationContext.Empty, question, [], hits, day, now),
                cancellationToken)
            .ConfigureAwait(false);

        if (completion.IsFailure)
        {
            return completion.Error;
        }

        var outcome = completion.Value;
        var citations = CitationResolver.Resolve(outcome.Response.Text, outcome.Excerpts, ai.MaxImagesInReply);

        var answer = new AiAnswer(
            citations.Text,
            [.. citations.Images.Select(image => new AiAnswerImage(
                image.Number, image.ImageUrl, image.Excerpt.SourceUrl, image.Excerpt.PictureTitle, image.Position, image.Count,
                image.Excerpt.Caption ?? image.Excerpt.GeneratedCaption))],
            [.. citations.Sources.Select(citation => new AiAnswerSource(citation.Number, citation.Excerpt.Label, citation.Excerpt.SourceUrl, citation.IsImage))],
            outcome.Response.ModelUsed,
            ConversationId: 0,
            Depth: 0,
            IsLongThread: false,
            RetrievedSourceUrls: [.. outcome.Excerpts.Select(excerpt => excerpt.SourceUrl).Distinct(StringComparer.Ordinal)],
            CitedSourceUrls: [.. citations.Sources.Select(citation => citation.Excerpt.SourceUrl).Distinct(StringComparer.Ordinal)],
            outcome.ToolCalls);

        LogReviewed(logger, token, rounds.Count, rounds.Count(round => round.MissedCountry), hits.Count, outcome.Response.ModelUsed);
        return new RoundReview(answer, token, rounds);
    }

    /// <summary>The challenge named, or the club's latest daily challenges, best-known first.</summary>
    private async Task<Result<IReadOnlyList<string>>> ResolveTokensAsync(string? challenge, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(challenge))
        {
            return ChallengeLink.TryReadToken(challenge) is { } token
                ? Result<IReadOnlyList<string>>.Success([token])
                : Error.Validation(RoundReviewErrorCodes.NoChallenge,
                    "That is no challenge link or token. Paste the challenge link (geoguessr.com/challenge/…) or its results link.");
        }

        var links = await challenges.ReadLatestClubChallengeLinksAsync(cancellationToken).ConfigureAwait(false);
        return links.Count == 0
            ? Error.NotFound(RoundReviewErrorCodes.NoChallenge, "There is no daily challenge to review yet; paste a challenge link.")
            : Result<IReadOnlyList<string>>.Success([.. links.Select(link => link.ChallengeId).Distinct(StringComparer.Ordinal)]);
    }

    /// <summary>The member's game in the first of the challenges they appear in.</summary>
    private async Task<Result<(string Token, ChallengeResultGameDto Game)>> FindGameAsync(
        IGeoGuessrClient client,
        IReadOnlyList<string> tokens,
        string userId,
        AiRoundReviewConfiguration settings,
        CancellationToken cancellationToken)
    {
        var unreadable = 0;
        foreach (var token in tokens)
        {
            ChallengeResultHighscoresDto highscores;
            try
            {
                highscores = await client.ReadHighscoresAsync(
                        token,
                        new ReadHighscoresQueryParams { Friends = false, Limit = settings.HighscoresLimit, MinRounds = 1 },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                LogResultsUnavailable(logger, token, exception);
                unreadable++;
                continue;
            }

            var mine = highscores.Items.FirstOrDefault(item => string.Equals(item.Game.Player.Id, userId, StringComparison.Ordinal));
            if (mine is not null)
            {
                return (token, mine.Game);
            }
        }

        if (unreadable == tokens.Count)
        {
            return Error.Unexpected(RoundReviewErrorCodes.ResultsUnavailable, "GeoGuessr's results could not be read just now; try again in a while.");
        }

        return Error.NotFound(RoundReviewErrorCodes.NotPlayed,
            tokens.Count == 1
                ? "You are not on this challenge's results: play it first, or check the link."
                : "You are not on the latest daily challenges' results yet; play one first, or paste a challenge link.");
    }

    /// <summary>Each round with where it was and where the player guessed, placed by GeoGuessr's code or the boundaries.</summary>
    private async Task<IReadOnlyList<ReviewedRound>> ReadRoundsAsync(ChallengeResultGameDto game, int maxRounds, CancellationToken cancellationToken)
    {
        var rounds = game.Rounds ?? [];
        var guesses = game.Player.Guesses ?? [];
        var reviewed = new List<ReviewedRound>();

        for (var index = 0; index < rounds.Count && reviewed.Count < Math.Max(1, maxRounds); index++)
        {
            var round = rounds[index];
            var guess = index < guesses.Count ? guesses[index] : null;

            var place = round is { Lat: { } lat, Lng: { } lng }
                ? await boundaries.LocateAsync(lat, lng, cancellationToken).ConfigureAwait(false)
                : null;
            var countryCode = Code(round.StreakLocationCode) ?? place?.CountryCode;

            RegionPlace? guessPlace = null;
            var guessed = guess is { Lat: { } glat, Lng: { } glng } && guess.SkippedRound != true && !(guess.TimedOut == true && guess.TimedOutWithGuess != true);
            if (guessed)
            {
                guessPlace = await boundaries.LocateAsync(guess!.Lat!.Value, guess.Lng!.Value, cancellationToken).ConfigureAwait(false);
            }

            var guessCountry = guessed ? Code(guess!.StreakLocationCode) ?? guessPlace?.CountryCode : null;

            double? distanceKm = null;
            if (guessed)
            {
                if (guess!.DistanceInMeters is { } metres)
                {
                    distanceKm = metres / 1000d;
                }
                else if (round is { Lat: { } rlat, Lng: { } rlng })
                {
                    distanceKm = Haversine.Kilometres(rlat, rlng, guess.Lat!.Value, guess.Lng!.Value);
                }
            }

            reviewed.Add(new ReviewedRound(index + 1, countryCode, place?.RegionName, guessCountry, guessPlace?.RegionName, distanceKm, guess?.RoundScoreInPoints));
        }

        return reviewed;
    }

    /// <summary>
    /// The guides' excerpts for each round's place: every round's wording embedded in one request, each
    /// searched within its country (under every spelling the index files it under), the rounds taking
    /// turns until <see cref="AiRoundReviewConfiguration.MaxExcerpts"/>. A failed embedding leaves the
    /// model with the rounds alone, which it can still speak to from the excerpts it searches for itself.
    /// </summary>
    private async Task<IReadOnlyList<KnowledgeHit>> RetrieveAsync(
        IReadOnlyList<ReviewedRound> rounds,
        AiRoundReviewConfiguration settings,
        CancellationToken cancellationToken)
    {
        var placed = rounds.Where(round => round.CountryCode is not null).ToList();
        if (placed.Count == 0)
        {
            return [];
        }

        var embedded = await embedder
            .EmbedAsync([.. placed.Select(round => (EmbeddingInput)new TextEmbeddingInput(RoundReviewPrompt.SearchText(round)))], cancellationToken)
            .ConfigureAwait(false);

        if (embedded.IsFailure || embedded.Value.Count != placed.Count)
        {
            LogRetrievalFailed(logger, embedded.IsFailure ? embedded.Error.Message : "the embedder returned the wrong number of vectors");
            return [];
        }

        var gazetteer = await countryDirectory.ReadAsync(cancellationToken).ConfigureAwait(false);

        var perRound = new List<IReadOnlyList<KnowledgeHit>>();
        for (var index = 0; index < placed.Count; index++)
        {
            var round = placed[index];
            var spellings = gazetteer.Resolve(IsoCountries.NameOf(round.CountryCode!)) is { Count: 1 } mentions
                ? mentions[0].IndexNames
                : [IsoCountries.NameOf(round.CountryCode!).ToLowerInvariant()];

            IReadOnlyList<KnowledgeHit> found = [];
            foreach (var spelling in spellings)
            {
                found = await knowledgeIndex.SearchAsync(
                        new KnowledgeQuery { TextVector = embedded.Value[index], Country = spelling, Limit = settings.ExcerptsPerRound },
                        cancellationToken)
                    .ConfigureAwait(false);
                if (found.Count > 0)
                {
                    break;
                }
            }

            perRound.Add(found);
        }

        // Round-robin, so a review of five countries offers something for each before more of any.
        var hits = new List<KnowledgeHit>();
        var seen = new HashSet<Guid>();
        for (var rank = 0; hits.Count < settings.MaxExcerpts && perRound.Any(list => rank < list.Count); rank++)
        {
            foreach (var list in perRound)
            {
                if (rank < list.Count && seen.Add(list[rank].Id) && hits.Count < settings.MaxExcerpts)
                {
                    hits.Add(list[rank]);
                }
            }
        }

        return hits;
    }

    private static string? Code(string? streakLocationCode) =>
        streakLocationCode is { Length: 2 } code && IsoCountries.IsCountry(code) ? code.ToUpperInvariant() : null;

    [LoggerMessage(LogLevel.Information,
        "Reviewed challenge {Token}: {Rounds} round(s), {Missed} with the wrong country, {Excerpts} excerpt(s) offered, answered by {Model}.")]
    static partial void LogReviewed(ILogger logger, string token, int rounds, int missed, int excerpts, string model);

    [LoggerMessage(LogLevel.Warning, "The results of challenge {Token} could not be read.")]
    static partial void LogResultsUnavailable(ILogger logger, string token, Exception exception);

    [LoggerMessage(LogLevel.Warning, "The rounds' searches could not be embedded ({Reason}); the review goes to the model without excerpts.")]
    static partial void LogRetrievalFailed(ILogger logger, string reason);
}

/// <summary>Great-circle distance, for a guess GeoGuessr gave no distance for.</summary>
internal static class Haversine
{
    private const double EarthRadiusKm = 6371.0088;

    public static double Kilometres(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = Radians(lat2 - lat1);
        var dLon = Radians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(Radians(lat1)) * Math.Cos(Radians(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180;
}
