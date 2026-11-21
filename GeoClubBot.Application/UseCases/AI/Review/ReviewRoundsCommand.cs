using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Conversations;
using UseCases.UseCases.AI.Progress;
using UseCases.UseCases.AI.StreetView;
using UseCases.UseCases.AI.Tools;
using Utilities;

namespace UseCases.UseCases.AI.Review;

/// <summary>
/// Reviews a played game round by round — which clues each round showed that point to its location, and
/// what to study for the countries missed — or explains one Street View place.
/// </summary>
/// <param name="DiscordUserId">The member asking; their linked GeoGuessr account is the player of a challenge.</param>
/// <param name="Link">
/// A challenge, game or results link or token, or a Google Maps Street View link; null reviews the club's
/// latest daily challenge the member played.
/// </param>
public sealed record ReviewRoundsCommand(ulong DiscordUserId, string? Link) : ICommand<Result<RoundReview>>;

/// <param name="Answer">The review, shaped like an answer: text, pictures, sources.</param>
/// <param name="Subject">What was reviewed.</param>
/// <param name="Rounds">The rounds as the review read them.</param>
public sealed record RoundReview(AiAnswer Answer, ReviewSubject Subject, IReadOnlyList<ReviewedRound> Rounds)
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
    public const string Unfinished = "ai.review_unfinished";
    public const string NoPlace = "ai.review_no_place";
}

/// <summary>
/// Reads what the link names — the member's game of a challenge (read with the main club's account, as
/// the country challenges are), any single game by its token, or a Street View place from a Maps link —
/// places each round and guess in a country and region (GeoGuessr's own country code where it gives one,
/// the Natural Earth boundaries otherwise), searches the guides for each round's clues
/// (<see cref="RoundClueFinder"/>), has a free vision model check each round's panorama against them
/// (<see cref="ClueSightCheck"/>), and asks the model once, without tools, with the clues each round
/// was seen to show (<see cref="ReviewExcerpts"/>, <see cref="RoundReviewPrompt"/>). A game still being
/// played is refused: its last round is the one on the player's screen.
///
/// Costs the rounds' embedding request (one per 32 searches) and the chat request, claimed up front,
/// plus one vision request per round, claimed by the check.
/// </summary>
public sealed partial class ReviewRoundsHandler(
    IGeoGuessrUserRepository users,
    IClubChallengeRepository challenges,
    IGeoGuessrClientFactory geoGuessrClientFactory,
    IRegionBoundaries boundaries,
    IStreetViewGuesser streetViewGuesser,
    IMapsLinkResolver mapsLinks,
    RoundClueFinder clueFinder,
    ClueSightCheck sightCheck,
    IImageRelay imageRelay,
    AiAnswerLoop answerLoop,
    IAiBudgetRepository budget,
    IOptions<GeoGuessrConfiguration> geoGuessrConfiguration,
    IOptions<AiConfiguration> aiConfiguration,
    IOptions<AiRoundReviewConfiguration> configuration,
    TimeProvider timeProvider,
    ILogger<ReviewRoundsHandler> logger)
    : IRequestHandler<ReviewRoundsCommand, Result<RoundReview>>
{
    /// <summary>Discord shows at most ten pictures under a message: the round views and the guides' share them.</summary>
    private const int MaxPicturesPerMessage = 10;

    public async Task<Result<RoundReview>> Handle(ReviewRoundsCommand request, CancellationToken cancellationToken)
    {
        var settings = configuration.Value;
        var ai = aiConfiguration.Value;
        if (!ai.Active || !settings.Enabled)
        {
            return Error.Validation(RoundReviewErrorCodes.Disabled, "The round review is turned off.");
        }

        var link = ReviewLinks.Read(request.Link);
        if (link is null && !string.IsNullOrWhiteSpace(request.Link))
        {
            return Error.Validation(RoundReviewErrorCodes.NoChallenge,
                "That is no link I can review. Paste a challenge or game link (geoguessr.com/challenge/…, /game/… or /results/…), or a Google Maps Street View link.");
        }

        var user = await users.ReadUserByDiscordUserIdAsync(request.DiscordUserId, cancellationToken).ConfigureAwait(false);

        Result<(ReviewSubject Subject, IReadOnlyList<ReviewedRound> Rounds, ChallengeResultGameDto? Game)> read;
        if (link is { IsStreetView: true })
        {
            read = await ReadPlaceAsync(link, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            Result<(ReviewSubject Subject, ChallengeResultGameDto Game)> game;
            using (AiProgress.Step(AiProgressKind.ReadingChallenge))
            {
                game = await ReadGameAsync(link, user, settings, cancellationToken).ConfigureAwait(false);
            }

            if (game.IsFailure)
            {
                return game.Error;
            }

            if (!game.Value.Game.IsFinished)
            {
                return Error.Validation(RoundReviewErrorCodes.Unfinished,
                    "That game is still being played, and its last round is still on screen. Review it once it is finished.");
            }

            IReadOnlyList<ReviewedRound> placed;
            using (AiProgress.Step(AiProgressKind.PlacingRounds))
            {
                placed = await ReadRoundsAsync(game.Value.Game, settings.MaxRounds, cancellationToken).ConfigureAwait(false);
            }

            read = (game.Value.Subject, placed, game.Value.Game);
        }

        if (read.IsFailure)
        {
            return read.Error;
        }

        var (subject, rounds, played) = read.Value;
        if (rounds.Count == 0)
        {
            return Error.NotFound(RoundReviewErrorCodes.NoRounds,
                "GeoGuessr's results carried no rounds for this game, so there is nothing to review.");
        }

        var now = timeProvider.GetUtcNow();
        var day = DateOnly.FromDateTime(now.UtcDateTime);
        var upFront = clueFinder.EmbeddingRequests(rounds) + 1;
        if (!await budget.TryReserveRequestsAsync(day, upFront, ai.OpenRouter.DailyRequestBudget, cancellationToken).ConfigureAwait(false))
        {
            return AiAnswerErrors.BudgetExhausted;
        }

        // GeoNN looks at the panoramas while the guides are searched and the rounds checked; it costs no AI request.
        var reading = ReadPanoramasAsync(played, rounds, settings.StreetViewGuesser, cancellationToken);

        IReadOnlyList<IReadOnlyList<RoundClue>> clues;
        using (AiProgress.Step(AiProgressKind.SearchingGuides))
        {
            clues = await clueFinder.FindAsync(rounds, cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<PlaceSight?> sights;
        using (AiProgress.Step(AiProgressKind.LookingAtRounds, rounds.Count == 1 ? "the panorama" : $"{rounds.Count} rounds"))
        {
            sights = await LookAsync(rounds, clues, day, cancellationToken).ConfigureAwait(false);
        }

        rounds = await reading.ConfigureAwait(false);
        var (hits, evidenced) = ReviewExcerpts.Assemble(
            rounds, clues, sights, settings.WellPlayedPoints, settings.SightCheck.UnseenCluesForMissedRounds, settings.MaxExcerpts);
        rounds = evidenced;

        var question = RoundReviewPrompt.Build(rounds, subject, settings.WellPlayedPoints);
        var completion = await answerLoop.AnswerAsync(
                new AnswerRequest(ConversationContext.Empty, question, [], hits, day, now, AllowTools: false),
                cancellationToken)
            .ConfigureAwait(false);

        if (completion.IsFailure)
        {
            return completion.Error;
        }

        var views = settings.ShowRoundViews
            ? await StoreViewsAsync(rounds, sights, cancellationToken).ConfigureAwait(false)
            : [];

        var outcome = completion.Value;
        var maxGuidePictures = Math.Max(0, Math.Min(ai.MaxImagesInReply, MaxPicturesPerMessage - views.Count));
        var citations = CitationResolver.Resolve(outcome.Response.Text, outcome.Excerpts, maxGuidePictures);

        var images = citations.Images.Select(image => new AiAnswerImage(
                image.Number, image.ImageUrl, image.Excerpt.SourceUrl, image.Excerpt.PictureTitle, image.Position, image.Count,
                image.Excerpt.Caption ?? image.Excerpt.GeneratedCaption))
            .ToList();
        var sources = citations.Sources
            .Select(citation => new AiAnswerSource(citation.Number, citation.Excerpt.Label, citation.Excerpt.SourceUrl, citation.IsImage))
            .ToList();

        // Each round's view, numbered after the guides and listed with them, its line leading to the round in Google Maps.
        var marker = Math.Max(sources.Max(source => source.Marker) ?? 0, images.Count == 0 ? 0 : images.Max(image => image.Marker));
        foreach (var (round, url) in views)
        {
            marker++;
            var title = round.Played ? $"Round {round.Number}: {round.PlaceName}" : $"The place: {round.PlaceName}";
            images.Add(new AiAnswerImage(marker, url, round.Panorama!.MapsUrl, title, Caption: ViewCaption(round)));
            sources.Add(new AiAnswerSource(marker, $"{title} (Google Maps)", round.Panorama.MapsUrl, IsImage: true));
        }

        var notices = new List<string>();
        if (rounds.Any(round => round.Evidence is { Checked: true }))
        {
            notices.Add("What each round showed was checked by a free vision model against the guides' clues; it can miss or mistake one.");
        }

        if (rounds.FirstOrDefault(round => round.Reading is not null)?.Reading is { } geoNn)
        {
            notices.Add($"Each round's panorama was also looked at by GeoNN ({geoNn.Model}), a Street View model; what it saw is not from the guides.");
        }

        var answer = new AiAnswer(
            citations.Text,
            images,
            sources,
            outcome.Response.ModelUsed,
            ConversationId: 0,
            Depth: 0,
            IsLongThread: false,
            RetrievedSourceUrls: [.. outcome.Excerpts.Select(excerpt => excerpt.SourceUrl).Distinct(StringComparer.Ordinal)],
            CitedSourceUrls: [.. citations.Sources.Select(citation => citation.Excerpt.SourceUrl).Distinct(StringComparer.Ordinal)],
            outcome.ToolCalls)
        {
            Notices = notices
        };

        LogReviewed(logger, subject.Describe(), rounds.Count, rounds.Count(round => round.MissedCountry),
            rounds.Count(round => round.Evidence is { Checked: true }), rounds.Sum(round => round.Evidence?.Seen.Count ?? 0),
            hits.Count, outcome.Response.ModelUsed);
        return new RoundReview(answer, subject, rounds);
    }

    /// <summary>The game the link names: a single game by its token, the member's game of a challenge, or of the latest daily challenges.</summary>
    private async Task<Result<(ReviewSubject Subject, ChallengeResultGameDto Game)>> ReadGameAsync(
        ReviewLink? link,
        GeoGuessrUser? user,
        AiRoundReviewConfiguration settings,
        CancellationToken cancellationToken)
    {
        var client = geoGuessrClientFactory.CreateClient(geoGuessrConfiguration.Value.MainClub.ClubId);

        // A results link or a bare token may be either; a game is one read, and needs no linked account.
        if (link is { Kind: ReviewLinkKind.Game or ReviewLinkKind.Results })
        {
            ChallengeResultGameDto? game;
            try
            {
                game = await client.ReadGameAsync(link.Value, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                LogResultsUnavailable(logger, link.Value, exception);
                return Error.Unexpected(RoundReviewErrorCodes.ResultsUnavailable, "GeoGuessr's game could not be read just now; try again in a while.");
            }

            if (game is not null)
            {
                var own = user is not null && string.Equals(game.Player.Id, user.UserId, StringComparison.Ordinal);
                return (new ReviewSubject(ReviewSubjectKind.Game, link.Value, game.Player.Nick, own, game.MapName), game);
            }

            if (link.Kind == ReviewLinkKind.Game)
            {
                return Error.NotFound(RoundReviewErrorCodes.NoChallenge, "GeoGuessr knows no game by that link; check it and try again.");
            }
        }

        if (user is null)
        {
            return Error.Validation(RoundReviewErrorCodes.NotLinked,
                "Link your GeoGuessr account first (`/link`), so I know which game of the challenge is yours. "
                + "A game link (geoguessr.com/game/…) or a Street View link works without it.");
        }

        var tokens = await ResolveTokensAsync(link, cancellationToken).ConfigureAwait(false);
        if (tokens.IsFailure)
        {
            return tokens.Error;
        }

        var found = await FindGameAsync(client, tokens.Value, user.UserId, settings, cancellationToken).ConfigureAwait(false);
        return found.IsFailure
            ? found.Error
            : (new ReviewSubject(ReviewSubjectKind.Challenge, found.Value.Token, found.Value.Game.Player.Nick, IsOwnGame: true, found.Value.Game.MapName),
                found.Value.Game);
    }

    /// <summary>The place a Maps link names, as one round that was not played.</summary>
    private async Task<Result<(ReviewSubject Subject, IReadOnlyList<ReviewedRound> Rounds, ChallengeResultGameDto? Game)>> ReadPlaceAsync(
        ReviewLink link,
        CancellationToken cancellationToken)
    {
        string? url = link.Value;
        if (link.Kind == ReviewLinkKind.StreetViewShortLink)
        {
            using (AiProgress.Step(AiProgressKind.OpeningMapsLink))
            {
                url = await mapsLinks.ResolveAsync(link.Value, cancellationToken).ConfigureAwait(false);
            }

            if (url is null)
            {
                return Error.NotFound(RoundReviewErrorCodes.NoPlace, "That Maps link could not be opened; check it, or paste the full link from the address bar.");
            }
        }

        if (StreetViewLink.Read(url) is not { } spot)
        {
            return Error.Validation(RoundReviewErrorCodes.NoPlace, "That Maps link names no place I can read; paste the link of a Street View view.");
        }

        if (spot.PanoId is null)
        {
            return Error.Validation(RoundReviewErrorCodes.NoPlace,
                "That Maps link shows a map, not Street View. Open Street View there and copy that link.");
        }

        var place = await boundaries.LocateAsync(spot.Latitude, spot.Longitude, cancellationToken).ConfigureAwait(false);
        if (place is null)
        {
            return Error.NotFound(RoundReviewErrorCodes.NoPlace, "I could not tell which country that place is in, so I have no guides to check it against.");
        }

        var round = new ReviewedRound(1, place.CountryCode, place.RegionName, null, null, null, null)
        {
            Played = false,
            Panorama = new RoundPanorama(spot.PanoId, StreetViewAngles.ForSpot(spot), spot.MapsUrl)
        };

        return (new ReviewSubject(ReviewSubjectKind.StreetView, null, null, IsOwnGame: true), [round], null);
    }

    /// <summary>The challenge named, or the club's latest daily challenges, best-known first.</summary>
    private async Task<Result<IReadOnlyList<string>>> ResolveTokensAsync(ReviewLink? link, CancellationToken cancellationToken)
    {
        if (link is not null)
        {
            return Result<IReadOnlyList<string>>.Success([link.Value]);
        }

        var links = await challenges.ReadLatestClubChallengeLinksAsync(cancellationToken).ConfigureAwait(false);
        return links.Count == 0
            ? Error.NotFound(RoundReviewErrorCodes.NoChallenge, "There is no daily challenge to review yet; paste a challenge or game link.")
            : Result<IReadOnlyList<string>>.Success([.. links.Select(challengeLink => challengeLink.ChallengeId).Distinct(StringComparer.Ordinal)]);
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
                : "You are not on the latest daily challenges' results yet; play one first, or paste a challenge or game link.");
    }

    /// <summary>Each round with where it was and where the player guessed, placed by GeoGuessr's code or the boundaries.</summary>
    private async Task<IReadOnlyList<ReviewedRound>> ReadRoundsAsync(ChallengeResultGameDto game, int maxRounds, CancellationToken cancellationToken)
    {
        var rounds = game.Rounds ?? [];
        var guesses = game.Player.Guesses ?? [];
        var reviewed = new List<ReviewedRound>();
        var mode = game.ForbidRotating == true ? PanoramaViewMode.FixedView : PanoramaViewMode.FullPanorama;

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

            RoundPanorama? panorama = null;
            if (GeoGuessrPanoId.Decode(round.PanoId) is { } panoId && round is { Lat: { } plat, Lng: { } plng })
            {
                var angles = StreetViewAngles.ForRound(mode, round.Heading, round.Pitch, round.Zoom);
                var spot = new StreetViewSpot(panoId, plat, plng, angles[0].Heading, angles[0].Pitch, angles[0].FieldOfView);
                panorama = new RoundPanorama(panoId, angles, spot.MapsUrl);
            }

            reviewed.Add(new ReviewedRound(index + 1, countryCode, place?.RegionName, guessCountry, guessPlace?.RegionName, distanceKm, guess?.RoundScoreInPoints)
            {
                Panorama = panorama
            });
        }

        return reviewed;
    }

    /// <summary>Each round's panorama, checked against its clues; null for a round with no panorama or no country.</summary>
    private async Task<IReadOnlyList<PlaceSight?>> LookAsync(
        IReadOnlyList<ReviewedRound> rounds,
        IReadOnlyList<IReadOnlyList<RoundClue>> clues,
        DateOnly day,
        CancellationToken cancellationToken)
    {
        var looked = rounds
            .Select((round, index) => (Index: index, Round: round))
            .Where(entry => entry.Round is { Panorama: not null, CountryCode: not null })
            .ToList();
        if (looked.Count == 0)
        {
            return [.. rounds.Select(_ => (PlaceSight?)null)];
        }

        var requests = looked.Select(entry => new SightRequest(
                entry.Round.Panorama!.PanoId,
                entry.Round.Panorama.Angles,
                entry.Round.CountryName,
                entry.Round.Region,
                entry.Round.MissedCountry ? entry.Round.GuessCountryName : null,
                [.. clues[entry.Index].Select(clue => clue.Hit)]))
            .ToList();

        var sights = await sightCheck.LookAsync(requests, day, cancellationToken).ConfigureAwait(false);

        var byRound = new PlaceSight?[rounds.Count];
        for (var position = 0; position < looked.Count; position++)
        {
            byRound[looked[position].Index] = sights[position];
        }

        return byRound;
    }

    /// <summary>
    /// Each round's most telling view, stored where the answer can upload it; none when the relay is
    /// off, since Google's endpoint would refuse Discord as it refuses the bot without a browser's name.
    /// </summary>
    private async Task<IReadOnlyList<(ReviewedRound Round, string Url)>> StoreViewsAsync(
        IReadOnlyList<ReviewedRound> rounds,
        IReadOnlyList<PlaceSight?> sights,
        CancellationToken cancellationToken)
    {
        if (!imageRelay.IsEnabled)
        {
            return [];
        }

        var stored = new List<(ReviewedRound Round, string Url)>();
        for (var index = 0; index < rounds.Count && stored.Count < MaxPicturesPerMessage; index++)
        {
            if (rounds[index].Panorama is null || index >= sights.Count || sights[index]?.BestPicture is not { } picture)
            {
                continue;
            }

            var url = await imageRelay.StoreAsync(picture.Content, picture.ContentType, cancellationToken).ConfigureAwait(false);
            if (url.IsSuccess)
            {
                stored.Add((rounds[index], url.Value));
            }
        }

        return stored;
    }

    private static string ViewCaption(ReviewedRound round)
    {
        if (!round.Played)
        {
            return "The view the explanation was checked on. The link opens it in Google Maps.";
        }

        var guess = round.GuessCountryName is null ? "No guess" : $"Guessed {round.GuessCountryName}";
        return $"{guess}. The view the review was checked on; the link opens the round in Google Maps.";
    }

    /// <summary>
    /// Each round with what GeoNN made of its panorama, shown the way the game showed it: one view at the
    /// round's heading, pitch and zoom when rotating was forbidden, else the whole panorama. Rounds it could
    /// not read, or every round when it is not set up, are left as they were.
    /// </summary>
    private async Task<IReadOnlyList<ReviewedRound>> ReadPanoramasAsync(
        ChallengeResultGameDto? game,
        IReadOnlyList<ReviewedRound> reviewed,
        AiStreetViewGuesserConfiguration settings,
        CancellationToken cancellationToken)
    {
        if (!streetViewGuesser.IsAvailable || game?.Rounds is not { Count: > 0 } rounds)
        {
            return reviewed;
        }

        using var step = AiProgress.Step(AiProgressKind.ReadingPanoramas);
        var mode = game.ForbidRotating == true ? PanoramaViewMode.FixedView : PanoramaViewMode.FullPanorama;
        var readings = await Task.WhenAll(reviewed.Select(async round =>
        {
            var played = rounds[round.Number - 1];
            if (GeoGuessrPanoId.Decode(played.PanoId) is not { } panoId || played is not { Lat: { } lat, Lng: { } lng })
            {
                return round;
            }

            var guess = await streetViewGuesser
                .GuessAsync(new PanoramaView(panoId, mode, played.Heading, played.Pitch, played.Zoom), cancellationToken)
                .ConfigureAwait(false);
            return guess is null
                ? round
                : round with
                {
                    Reading = new PanoramaReading(
                        [.. guess.Countries.Take(Math.Max(1, settings.CountriesShown))],
                        Haversine.Kilometres(lat, lng, guess.Latitude, guess.Longitude),
                        guess.Model)
                };
        })).ConfigureAwait(false);

        LogPanoramasRead(logger, readings.Count(round => round.Reading is not null), readings.Length,
            readings.Count(round => round.Reading?.TopCountryCode is { } top && string.Equals(top, round.CountryCode, StringComparison.OrdinalIgnoreCase)));
        return readings;
    }

    private static string? Code(string? streakLocationCode) =>
        streakLocationCode is { Length: 2 } code && IsoCountries.IsCountry(code) ? code.ToUpperInvariant() : null;

    [LoggerMessage(LogLevel.Information,
        "Reviewed {Subject}: {Rounds} round(s), {Missed} with the wrong country, {Checked} checked by sight with {Seen} clue(s) seen, {Excerpts} excerpt(s) offered, answered by {Model}.")]
    static partial void LogReviewed(ILogger logger, string subject, int rounds, int missed, int @checked, int seen, int excerpts, string model);

    [LoggerMessage(LogLevel.Warning, "The results of {Token} could not be read.")]
    static partial void LogResultsUnavailable(ILogger logger, string token, Exception exception);

    [LoggerMessage(LogLevel.Information, "GeoNN read {Read} of {Rounds} round panorama(s); its likeliest country was right in {Right}.")]
    static partial void LogPanoramasRead(ILogger logger, int read, int rounds, int right);
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
