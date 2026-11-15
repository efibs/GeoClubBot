using Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Progress;
using Utilities;

namespace UseCases.UseCases.AI.Tools;

/// <param name="Identification">What the service made of the screenshot: the candidates, or that it saw no plant.</param>
/// <param name="Range">Where the identified plant is recorded, with its map, when the best candidate was sure enough and GBIF knows it.</param>
/// <param name="Notices">Lines for the member beneath the answer: why the identification could not be made, what it was weighed by, how to make it surer.</param>
public sealed record PlantIdentificationOffer(KnowledgeHit? Identification, KnowledgeHit? Range, IReadOnlyList<string> Notices)
{
    /// <summary>The excerpts to offer, the identification before the range; empty when there are only notices.</summary>
    public IReadOnlyList<KnowledgeHit> Hits =>
        new[] { Identification, Range }.OfType<KnowledgeHit>().ToList();
}

/// <summary>
/// Names the plant in a member's screenshot before the model answers, when the question asks what
/// plant it shows, and offers the identification — and where that plant grows, with the map — as
/// excerpts among the guides'.
///
/// Asked "what kind of cactus is this?" with a screenshot, the model named the species of a look-alike
/// guide picture and gave that guide's country (2026-11-09). An image-recognition service names the
/// plant with a score; the species it names goes to the same GBIF lookup a "where" question gets, so
/// the answer says what the plant is, how sure that is, and where it is found.
///
/// When the member says where the screenshot was taken — "just south of Boston", "this is Bulgaria",
/// in the question or earlier in the branch — the candidates are weighed by how often GBIF records each
/// there (<see cref="PlantLocationWeighting"/>): Pl@ntNet sees only the picture, and its world flora
/// leans European (#485). A follow-up that only says where ("it's near Boston though") weighs the
/// branch's screenshot again for that place, from the cached identification.
///
/// Costs no AI request; one identification of the day's Pl@ntNet allowance, which is why it runs only
/// for a question that asks about a plant and comes with a picture. Anything that goes wrong offers
/// nothing, or a notice, and the question is answered as before.
/// </summary>
public sealed partial class PlantIdentificationLookAhead(
    IPlantIdentifier identifier,
    IPlantRecordCounter recordCounter,
    ISpeciesRangeReader speciesRange,
    ISpeciesRangeMap speciesMap,
    KnownPlaceDirectory placeDirectory,
    IOptions<AiToolsConfiguration> toolsConfiguration,
    TimeProvider timeProvider,
    ILogger<PlantIdentificationLookAhead> logger)
{
    /// <summary>Said beneath the answer when the day's identifications are spent.</summary>
    public const string QuotaNotice =
        "Plant identification (Pl@ntNet) has used today's allowance; the plant was judged from the picture alone. It resets at midnight UTC.";

    /// <summary>Said beneath the answer when the service could not be reached.</summary>
    public const string UnavailableNotice =
        "Plant identification (Pl@ntNet) could not be reached; the plant was judged from the picture alone.";

    /// <summary>Said beneath an answer that could not name the plant: what makes Pl@ntNet surer.</summary>
    public const string CropTipNotice =
        "Tip: Pl@ntNet is far surer with a close crop of one plant (its leaves, needles, flowers or bark) than with a whole Street View frame.";

    /// <summary>Said beneath an answer that could not name the plant and did not know where it stood.</summary>
    public const string PlaceTipNotice =
        "Tip: say where the screenshot was taken (a country, a state or a nearby town) and the candidates are weighed by what grows there.";

    /// <summary>How long the whole look-ahead may take beside retrieval: the identification, the records, then one GBIF read.</summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(20);

    /// <summary>
    /// How long the place may take to read. The towns are read once at start-up; an answer asked before
    /// that is done is not held for them, and goes without the place.
    /// </summary>
    private static readonly TimeSpan PlaceDeadline = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The offer for <paramref name="question"/> asked about <paramref name="screenshotUrls"/> (views of
    /// one plant), or null when it asks no such thing, the feature is off, or nothing could be made of it.
    /// <paramref name="earlierQuestions"/> are the member's earlier messages in the branch, oldest first:
    /// where they said the screenshot was taken, and whether they asked about its plant.
    /// </summary>
    public async Task<PlantIdentificationOffer?> ReadAsync(
        string? question,
        IReadOnlyList<string> screenshotUrls,
        IReadOnlyList<string> earlierQuestions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(screenshotUrls);
        ArgumentNullException.ThrowIfNull(earlierQuestions);

        var settings = toolsConfiguration.Value.PlantIdentification;
        if (!settings.Enabled || !identifier.IsAvailable || screenshotUrls.Count == 0)
        {
            return null;
        }

        var asksHere = PlantIdentificationQuestion.Asks(question);
        if (!asksHere
            && (!settings.Location.Enabled
                || !PlantIdentificationQuestion.MayBeAboutThePlant(question)
                || !earlierQuestions.Any(PlantIdentificationQuestion.Asks)))
        {
            return null;
        }

        using var timeout = new CancellationTokenSource(Deadline, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        var pictures = screenshotUrls.Take(Math.Max(1, settings.MaxPictures)).ToList();

        // Asked outright, the picture goes to Pl@ntNet while the place is read: it never depends on the place.
        Task<Result<PlantIdentification>>? identifying = asksHere ? IdentifyAsync(pictures, linked.Token) : null;
        var reading = settings.Location.Enabled ? ReadPlaceAsync(question, earlierQuestions, linked.Token) : Task.FromResult<PlaceReading?>(null);

        Result<PlantIdentification> identified;
        PlaceReading? read;
        try
        {
            read = await reading.ConfigureAwait(false);

            // A follow-up about a screenshot asked about before: weighed again only when it says where. The
            // identification itself is the cached one, since it never depends on the place.
            if (identifying is null)
            {
                if (read is not { SaidInQuestion: true })
                {
                    return null;
                }

                LogFollowUpPlace(logger, read.Place.Name);
                identifying = IdentifyAsync(pictures, linked.Token);
            }

            identified = await identifying.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogTimedOut(logger, Deadline.TotalSeconds);
            return new PlantIdentificationOffer(null, null, [UnavailableNotice]);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(logger, exception);
            return new PlantIdentificationOffer(null, null, [UnavailableNotice]);
        }

        var place = read?.Place;

        if (identified.IsFailure)
        {
            return identified.Error.Code switch
            {
                PlantIdentificationErrorCodes.NotRecognised => Offer(PlantIdentificationExcerpt.NotRecognised(), null, [CropTipNotice], "no plant recognised"),
                PlantIdentificationErrorCodes.QuotaExceeded => Offer(null, null, [QuotaNotice], "the day's allowance is spent"),
                PlantIdentificationErrorCodes.ImageUnusable => Offer(null, null, [], $"the picture could not be sent ({identified.Error.Message})"),
                PlantIdentificationErrorCodes.Unavailable => null,
                _ => Offer(null, null, [UnavailableNotice], identified.Error.Message)
            };
        }

        var identification = identified.Value;
        LogIdentified(logger, identification.Best.ScientificName, identification.Best.Score, identification.Candidates.Count,
            identification.Genera.FirstOrDefault()?.Name, identification.Genera.FirstOrDefault()?.Score ?? 0, identification.RemainingRequests);

        var weighing = place is null
            ? PlantLocationWeighting.Weigh(identification, null, null, null, 0, 1)
            : await WeighAsync(identification, place, settings.Location, linked.Token, cancellationToken).ConfigureAwait(false);

        var hit = PlantIdentificationExcerpt.ToHit(
            identification, weighing, settings.MinConfidence, settings.MinImageScore, settings.MaxCandidates, settings.Location.RadiusKm);

        var identifiedPlant = weighing.IsIdentified(settings.MinConfidence, settings.MinImageScore);
        var notices = new List<string>();
        if (weighing is { IsWeighed: true, Place: { } weighedFor })
        {
            notices.Add(weighedFor.Precision == PlacePrecision.Country
                ? $"Plant candidates weighed by GBIF records {weighedFor.DescribeArea(settings.Location.RadiusKm)}."
                : $"Plant candidates weighed by GBIF records {weighedFor.DescribeArea(settings.Location.RadiusKm)} ({weighedFor.Name}).");
        }

        if (!identifiedPlant)
        {
            notices.Add(CropTipNotice);
            if (place is null && settings.Location.Enabled)
            {
                notices.Add(PlaceTipNotice);
            }
        }

        // Sure enough to be looked up where it grows: the countries and the map are what the member
        // asked for next, every time ("and where does it grow?").
        KnowledgeHit? range = null;
        if (identifiedPlant)
        {
            range = await ReadRangeAsync(weighing.Best.Candidate, linked.Token, cancellationToken).ConfigureAwait(false);
        }

        LogOffered(logger, weighing.Best.Candidate.ScientificName, weighing.Best.Share, identifiedPlant, place?.Name ?? "nowhere said", weighing.IsWeighed);
        return new PlantIdentificationOffer(hit, range, notices);
    }

    private PlantIdentificationOffer Offer(KnowledgeHit? identification, KnowledgeHit? range, IReadOnlyList<string> notices, string reason)
    {
        LogNotIdentified(logger, reason);
        return new PlantIdentificationOffer(identification, range, notices);
    }

    private async Task<Result<PlantIdentification>> IdentifyAsync(IReadOnlyList<string> pictures, CancellationToken cancellationToken)
    {
        using var step = AiProgress.Step(AiProgressKind.IdentifyingPlant);
        return await identifier.IdentifyAsync(pictures, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Where the member said the screenshot was taken — in the question, or else earlier in the branch —
    /// or null when they did not, or the places could not be read in time. Never throws.
    /// </summary>
    private async Task<PlaceReading?> ReadPlaceAsync(string? question, IReadOnlyList<string> earlierQuestions, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(PlaceDeadline, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            var places = await placeDirectory.ReadAsync(linked.Token).ConfigureAwait(false);
            var countries = IsoCountries.Gazetteer;

            // The question's own place, settled by the countries the branch named; else the branch's latest.
            var place = ScreenshotPlaceReader.Find(question, places, countries, earlierQuestions);
            return place is null
                ? null
                : new PlaceReading(place, SaidInQuestion: ScreenshotPlaceReader.Find(question, places, countries) is not null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogPlaceTimedOut(logger, PlaceDeadline.TotalSeconds);
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogPlaceFailed(logger, exception);
            return null;
        }
    }

    /// <summary>
    /// The candidates weighed by their records near the place and in its country, counted side by side;
    /// Pl@ntNet's order when the records near the place cannot be read. Never throws.
    /// </summary>
    private async Task<PlantWeighing> WeighAsync(
        PlantIdentification identification,
        ScreenshotPlace place,
        AiPlantLocationConfiguration settings,
        CancellationToken deadline,
        CancellationToken cancellationToken)
    {
        var keys = identification.Candidates.Select(candidate => candidate.GbifTaxonKey).OfType<long>().Distinct().ToList();
        var localArea = place.LocalArea(settings.RadiusKm);
        if (keys.Count == 0 || localArea is null)
        {
            return PlantLocationWeighting.Weigh(identification, place, null, null, settings.RecordsWeight, settings.UnrecordedFactor);
        }

        try
        {
            using var step = AiProgress.Step(AiProgressKind.CheckingWhatGrowsThere, place.ShortName);

            var localRead = recordCounter.CountAsync(keys, localArea, deadline);
            var countryRead = localArea is CountryRecordArea || place.CountryArea is not { } countryArea
                ? localRead
                : recordCounter.CountAsync(keys, countryArea, deadline);

            var local = await localRead.ConfigureAwait(false);
            var country = await countryRead.ConfigureAwait(false);

            if (local.IsFailure)
            {
                LogRecordsUnavailable(logger, place.Name, local.Error.Code);
                return PlantLocationWeighting.Weigh(identification, place, null, null, settings.RecordsWeight, settings.UnrecordedFactor);
            }

            // Coordinates far from any known town have no country to count in: the records near them still weigh.
            var countryCounts = place.CountryArea is null ? null : country.IsSuccess ? country.Value : null;
            return PlantLocationWeighting.Weigh(identification, place, local.Value, countryCounts, settings.RecordsWeight, settings.UnrecordedFactor);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogRecordsUnavailable(logger, place.Name, "timed out");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogRecordsFailed(logger, place.Name, exception);
        }

        return PlantLocationWeighting.Weigh(identification, place, null, null, settings.RecordsWeight, settings.UnrecordedFactor);
    }

    /// <summary>The plant's range by its scientific name, with its map, or null when GBIF does not know it or cannot be read.</summary>
    private async Task<KnowledgeHit?> ReadRangeAsync(PlantCandidate best, CancellationToken deadline, CancellationToken cancellationToken)
    {
        var rangeSettings = toolsConfiguration.Value.SpeciesRange;
        if (!rangeSettings.Enabled)
        {
            return null;
        }

        try
        {
            Result<SpeciesRange> read;
            using (AiProgress.Step(AiProgressKind.LookingUpPlant, best.ScientificName))
            {
                read = await speciesRange.ReadAsync(new SpeciesRangeQuery(best.ScientificName, null), deadline).ConfigureAwait(false);
            }

            if (read.IsFailure)
            {
                LogRangeUnavailable(logger, best.ScientificName, read.Error.Code);
                return null;
            }

            var range = read.Value;
            if (range.MatchedByCommonName || !range.Areas.Any(area => IsoCountries.IsCountry(area.Name)))
            {
                return null;
            }

            // On its own clock, like the "where" look-ahead's map: it never fails the identification.
            string? map = null;
            try
            {
                using var step = AiProgress.Step(AiProgressKind.DrawingRangeMap, range.CanonicalName);
                map = await speciesMap.DrawAsync(range, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                LogMapFailed(logger, range.CanonicalName, exception);
            }

            return SpeciesRangeExcerpt.ToHit(range, best.ScientificName, rangeSettings.MaxAreas, map);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogRangeUnavailable(logger, best.ScientificName, "timed out");
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogRangeFailed(logger, best.ScientificName, exception);
            return null;
        }
    }

    /// <param name="SaidInQuestion">Whether the question itself says where, rather than an earlier message.</param>
    private sealed record PlaceReading(ScreenshotPlace Place, bool SaidInQuestion);

    [LoggerMessage(LogLevel.Information,
        "Pl@ntNet identified the screenshot's plant as {ScientificName} at {Score:F2} ({Candidates} candidate(s); genus {Genus} at {GenusScore:F2}; {Remaining} identification(s) left today).")]
    static partial void LogIdentified(ILogger logger, string scientificName, double score, int candidates, string? genus, double genusScore, int? remaining);

    [LoggerMessage(LogLevel.Information,
        "Offered {ScientificName} (share {Share:F2}, identified: {Identified}) for a screenshot from {Place} (weighed by records: {Weighed}).")]
    static partial void LogOffered(ILogger logger, string scientificName, double share, bool identified, string place, bool weighed);

    [LoggerMessage(LogLevel.Information, "A follow-up says the screenshot is from {Place}; its plant's candidates are weighed again for that place.")]
    static partial void LogFollowUpPlace(ILogger logger, string place);

    [LoggerMessage(LogLevel.Information, "The screenshot's plant was not identified: {Reason}.")]
    static partial void LogNotIdentified(ILogger logger, string reason);

    [LoggerMessage(LogLevel.Warning, "Identifying the screenshot's plant took over {Seconds} s; the question is answered without it.")]
    static partial void LogTimedOut(ILogger logger, double seconds);

    [LoggerMessage(LogLevel.Warning, "Identifying the screenshot's plant failed unexpectedly; the question is answered without it.")]
    static partial void LogFailed(ILogger logger, Exception exception);

    [LoggerMessage(LogLevel.Information, "The places were not read within {Seconds} s; the plant is identified without where the screenshot was taken.")]
    static partial void LogPlaceTimedOut(ILogger logger, double seconds);

    [LoggerMessage(LogLevel.Warning, "Reading where the screenshot was taken failed unexpectedly; the plant is identified without it.")]
    static partial void LogPlaceFailed(ILogger logger, Exception exception);

    [LoggerMessage(LogLevel.Information, "GBIF's records near {Place} could not be read ({Reason}); the candidates keep Pl@ntNet's order.")]
    static partial void LogRecordsUnavailable(ILogger logger, string place, string reason);

    [LoggerMessage(LogLevel.Warning, "Counting the candidates' records near {Place} failed unexpectedly; they keep Pl@ntNet's order.")]
    static partial void LogRecordsFailed(ILogger logger, string place, Exception exception);

    [LoggerMessage(LogLevel.Information, "GBIF could not place {ScientificName} ({Reason}); the identification is offered without its range.")]
    static partial void LogRangeUnavailable(ILogger logger, string scientificName, string reason);

    [LoggerMessage(LogLevel.Warning, "Reading the range of {ScientificName} failed unexpectedly; the identification is offered without it.")]
    static partial void LogRangeFailed(ILogger logger, string scientificName, Exception exception);

    [LoggerMessage(LogLevel.Warning, "Drawing the map of {CanonicalName} failed unexpectedly; its range is offered without one.")]
    static partial void LogMapFailed(ILogger logger, string canonicalName, Exception exception);
}
