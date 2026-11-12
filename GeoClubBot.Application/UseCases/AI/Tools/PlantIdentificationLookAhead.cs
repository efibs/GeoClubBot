using Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI;
using Utilities;

namespace UseCases.UseCases.AI.Tools;

/// <param name="Identification">What the service made of the screenshot: the candidates, or that it saw no plant.</param>
/// <param name="Range">Where the identified plant is recorded, with its map, when the best candidate was sure enough and GBIF knows it.</param>
/// <param name="Notice">A line for the member, beneath the answer, when the identification could not be made for a reason worth telling them.</param>
public sealed record PlantIdentificationOffer(KnowledgeHit? Identification, KnowledgeHit? Range, string? Notice)
{
    /// <summary>The excerpts to offer, the identification before the range; empty when there is only a notice.</summary>
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
/// Costs no AI request; one identification of the day's Pl@ntNet allowance, which is why it runs only
/// for a question that asks about a plant and comes with a picture. Anything that goes wrong offers
/// nothing, or a notice, and the question is answered as before.
/// </summary>
public sealed partial class PlantIdentificationLookAhead(
    IPlantIdentifier identifier,
    ISpeciesRangeReader speciesRange,
    ISpeciesRangeMap speciesMap,
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

    /// <summary>How long the whole look-ahead may take beside retrieval: the identification, then one GBIF read.</summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(20);

    /// <summary>
    /// The offer for <paramref name="question"/> asked with <paramref name="screenshotUrl"/>, or null
    /// when it asks no such thing, the feature is off, or nothing could be made of it.
    /// </summary>
    public async Task<PlantIdentificationOffer?> ReadAsync(string? question, string? screenshotUrl, CancellationToken cancellationToken)
    {
        var settings = toolsConfiguration.Value.PlantIdentification;
        if (!settings.Enabled || !identifier.IsAvailable || string.IsNullOrWhiteSpace(screenshotUrl))
        {
            return null;
        }

        if (!PlantIdentificationQuestion.Asks(question))
        {
            return null;
        }

        using var timeout = new CancellationTokenSource(Deadline, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        Result<PlantIdentification> identified;
        try
        {
            identified = await identifier.IdentifyAsync(screenshotUrl, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogTimedOut(logger, Deadline.TotalSeconds);
            return new PlantIdentificationOffer(null, null, UnavailableNotice);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(logger, exception);
            return new PlantIdentificationOffer(null, null, UnavailableNotice);
        }

        if (identified.IsFailure)
        {
            return identified.Error.Code switch
            {
                PlantIdentificationErrorCodes.NotRecognised => Offer(PlantIdentificationExcerpt.NotRecognised(), null, null, "no plant recognised"),
                PlantIdentificationErrorCodes.QuotaExceeded => Offer(null, null, QuotaNotice, "the day's allowance is spent"),
                PlantIdentificationErrorCodes.ImageUnusable => Offer(null, null, null, $"the picture could not be sent ({identified.Error.Message})"),
                PlantIdentificationErrorCodes.Unavailable => null,
                _ => Offer(null, null, UnavailableNotice, identified.Error.Message)
            };
        }

        var identification = identified.Value;
        var best = identification.Best;
        LogIdentified(logger, best.ScientificName, best.Score, identification.Candidates.Count, identification.RemainingRequests);

        var hit = PlantIdentificationExcerpt.ToHit(identification, settings.MinConfidence, settings.MaxCandidates);

        // Sure enough to be looked up where it grows: the countries and the map are what the member
        // asked for next, every time ("and where does it grow?").
        KnowledgeHit? range = null;
        if (best.Score >= settings.MinConfidence)
        {
            range = await ReadRangeAsync(best, linked.Token, cancellationToken).ConfigureAwait(false);
        }

        return new PlantIdentificationOffer(hit, range, null);
    }

    private PlantIdentificationOffer Offer(KnowledgeHit? identification, KnowledgeHit? range, string? notice, string reason)
    {
        LogNotIdentified(logger, reason);
        return new PlantIdentificationOffer(identification, range, notice);
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
            var read = await speciesRange.ReadAsync(new SpeciesRangeQuery(best.ScientificName, null), deadline).ConfigureAwait(false);
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

    [LoggerMessage(LogLevel.Information,
        "Pl@ntNet identified the screenshot's plant as {ScientificName} at {Score:F2} ({Candidates} candidate(s); {Remaining} identification(s) left today).")]
    static partial void LogIdentified(ILogger logger, string scientificName, double score, int candidates, int? remaining);

    [LoggerMessage(LogLevel.Information, "The screenshot's plant was not identified: {Reason}.")]
    static partial void LogNotIdentified(ILogger logger, string reason);

    [LoggerMessage(LogLevel.Warning, "Identifying the screenshot's plant took over {Seconds} s; the question is answered without it.")]
    static partial void LogTimedOut(ILogger logger, double seconds);

    [LoggerMessage(LogLevel.Warning, "Identifying the screenshot's plant failed unexpectedly; the question is answered without it.")]
    static partial void LogFailed(ILogger logger, Exception exception);

    [LoggerMessage(LogLevel.Information, "GBIF could not place {ScientificName} ({Reason}); the identification is offered without its range.")]
    static partial void LogRangeUnavailable(ILogger logger, string scientificName, string reason);

    [LoggerMessage(LogLevel.Warning, "Reading the range of {ScientificName} failed unexpectedly; the identification is offered without it.")]
    static partial void LogRangeFailed(ILogger logger, string scientificName, Exception exception);

    [LoggerMessage(LogLevel.Warning, "Drawing the map of {CanonicalName} failed unexpectedly; its range is offered without one.")]
    static partial void LogMapFailed(ILogger logger, string canonicalName, Exception exception);
}
