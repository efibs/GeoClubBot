using Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI;
using Utilities;

namespace UseCases.UseCases.AI.Tools;

/// <param name="Hit">The plant's range as an excerpt, with its map when one could be drawn.</param>
/// <param name="LeadsTheGuides">
/// The excerpt answers the question's "where" and goes first: a plant's countries, asked of the world.
/// Counted within one country it follows the guides instead, which say what is seen there in coverage;
/// the records then add a map, not a verdict.
/// </param>
public sealed record PlantRangeOffer(KnowledgeHit Hit, bool LeadsTheGuides);

/// <summary>
/// Looks a plant up before the model answers, when a question asks where it is found, and offers its
/// range — and a map of it — as one excerpt among the guides'.
///
/// Asked of the world, the excerpt lists the plant's countries. Asked of one country ("where in the US
/// can you see cholla?"), it lists that country's states or provinces, and only when the database holds
/// at least <see cref="AiSpeciesRangeConfiguration.MinRecordsInCountry"/> records of the plant there:
/// below that the records say where somebody once looked, not where it grows. Members asked for the map
/// either way — "Please show the GBIF distribution", "I would have liked a distribution map"
/// (2026-11-09) — and until then a question naming a country was not looked up at all.
///
/// The species_range tool was there for exactly this, and was called in none of 49 replayed answers
/// (2026-10-30), with two prompt rewordings: given excerpts about India and Vietnam, the model took them
/// for the answer, and "Khasi pines are also found outside of India" stayed the rated complaint. The
/// lookup costs no AI request and runs while the question is searched, so asking it up front costs only
/// a GBIF read for questions that turn out not to be about a plant.
///
/// What it offers must be the plant asked about, since a wrong one would be cited as fact. The species
/// database already takes only names it can stand behind; on top of that a lone word left by dropping
/// "tree" must match a scientific name (<see cref="PlantRangeName.ScientificOnly"/>), and a taxon with
/// fewer than <see cref="MinRecords"/> records is not offered. Anything else that goes wrong — GBIF slow
/// or down, the question cancelled — offers nothing, and the question is answered as before.
/// </summary>
public sealed partial class PlantRangeLookAhead(
    ISpeciesRangeReader speciesRange,
    ISpeciesRangeMap speciesMap,
    IOptions<AiToolsConfiguration> toolsConfiguration,
    TimeProvider timeProvider,
    ILogger<PlantRangeLookAhead> logger)
{
    /// <summary>
    /// Fewest records offered. The plants asked about are well recorded (Khasi pine 1,061, Agave
    /// lechuguilla 9,076), while a name that matched something obscure has a handful.
    /// </summary>
    private const int MinRecords = 25;

    /// <summary>
    /// How long the lookup may take in all, beside retrieval's own second or two. GBIF answered in well
    /// under a second when measured; a question about something else can take up to six reads.
    /// </summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(6);

    /// <summary>The excerpt to offer for <paramref name="question"/>, or null when it asks no such thing or nothing could be found.</summary>
    public async Task<PlantRangeOffer?> ReadAsync(string? question, CancellationToken cancellationToken)
    {
        var settings = toolsConfiguration.Value.SpeciesRange;
        if (!settings.Enabled || !settings.LookUpWhereQuestions)
        {
            return null;
        }

        var asked = PlantRangeQuestion.Read(question);
        if (asked is null)
        {
            return null;
        }

        // One place has to be one country the database counts by; a region or "my area" is left to the guides.
        string? countryCode = null;
        if (asked.Place is { } place)
        {
            countryCode = IsoCountries.Resolve(place);
            if (countryCode is null)
            {
                LogNotACountry(logger, place);
                return null;
            }
        }

        var names = asked.Names;
        var minRecords = countryCode is null ? MinRecords : Math.Max(MinRecords, settings.MinRecordsInCountry);

        SpeciesRange? found = null;
        string? foundBy = null;

        using (var timeout = new CancellationTokenSource(Deadline, timeProvider))
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
        {
            try
            {
                foreach (var name in names)
                {
                    var read = await speciesRange.ReadAsync(new SpeciesRangeQuery(name.Name, countryCode), linked.Token)
                        .ConfigureAwait(false);

                    if (read.IsFailure)
                    {
                        if (read.Error.Type == ErrorType.NotFound)
                        {
                            continue;
                        }

                        LogUnavailable(logger, name.Name);
                        return null;
                    }

                    var range = read.Value;
                    if (name.ScientificOnly && range.MatchedByCommonName)
                    {
                        continue;
                    }

                    if (range.TotalRecords < minRecords
                        || (countryCode is null && !range.Areas.Any(area => IsoCountries.IsCountry(area.Name))))
                    {
                        LogTooFewRecords(logger, name.Name, range.CanonicalName, range.TotalRecords, countryCode ?? "the world", minRecords);
                        return null;
                    }

                    found = range;
                    foundBy = name.Name;
                    break;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                LogTimedOut(logger, names[0].Name, Deadline.TotalSeconds);
                return null;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, names[0].Name, exception);
                return null;
            }
        }

        if (found is null)
        {
            LogNotAPlant(logger, string.Join(", ", names.Select(name => name.Name)));
            return null;
        }

        LogFound(logger, foundBy!, found.CanonicalName, found.TotalRecords, countryCode ?? "the world");

        // On its own clock: the map is a dozen tiles, and never fails the lookup it illustrates.
        var map = await DrawAsync(found, cancellationToken).ConfigureAwait(false);

        return new PlantRangeOffer(
            SpeciesRangeExcerpt.ToHit(found, foundBy!, settings.MaxAreas, map),
            LeadsTheGuides: countryCode is null);
    }

    private async Task<string?> DrawAsync(SpeciesRange range, CancellationToken cancellationToken)
    {
        try
        {
            return await speciesMap.DrawAsync(range, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogMapFailed(logger, range.CanonicalName, exception);
            return null;
        }
    }

    [LoggerMessage(LogLevel.Information,
        "The question asks where {Name} is found: offering GBIF's {CanonicalName}, {Records} record(s) in {Scope}, before the model answers.")]
    static partial void LogFound(ILogger logger, string name, string canonicalName, long records, string scope);

    [LoggerMessage(LogLevel.Debug, "The question asks where a plant is found in '{Place}', which is no one country; left to the guides.")]
    static partial void LogNotACountry(ILogger logger, string place);

    [LoggerMessage(LogLevel.Warning, "Drawing the map of {CanonicalName} failed unexpectedly; its range is offered without one.")]
    static partial void LogMapFailed(ILogger logger, string canonicalName, Exception exception);

    [LoggerMessage(LogLevel.Debug, "The question asks where {Names} is found, which GBIF knows as no plant.")]
    static partial void LogNotAPlant(ILogger logger, string names);

    [LoggerMessage(LogLevel.Information,
        "The question asks where {Name} is found, read as {CanonicalName} with only {Records} record(s) in {Scope} (fewer than {MinRecords}); nothing offered.")]
    static partial void LogTooFewRecords(ILogger logger, string name, string canonicalName, long records, string scope, long minRecords);

    [LoggerMessage(LogLevel.Warning, "GBIF could not be read for '{Name}' before answering; the question is answered without it.")]
    static partial void LogUnavailable(ILogger logger, string name);

    [LoggerMessage(LogLevel.Warning, "Looking up '{Name}' in GBIF took over {Seconds} s; the question is answered without it.")]
    static partial void LogTimedOut(ILogger logger, string name, double seconds);

    [LoggerMessage(LogLevel.Warning, "Looking up '{Name}' in GBIF failed unexpectedly; the question is answered without it.")]
    static partial void LogFailed(ILogger logger, string name, Exception exception);
}
