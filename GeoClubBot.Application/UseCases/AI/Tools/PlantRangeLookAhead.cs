using Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI;
using Utilities;

namespace UseCases.UseCases.AI.Tools;

/// <summary>
/// Looks a plant up before the model answers, when a question asks where in the world it is found,
/// and offers its countries as one excerpt among the guides'.
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
    public async Task<KnowledgeHit?> ReadAsync(string? question, CancellationToken cancellationToken)
    {
        var settings = toolsConfiguration.Value.SpeciesRange;
        if (!settings.Enabled || !settings.LookUpWhereQuestions)
        {
            return null;
        }

        var names = PlantRangeQuestion.NamesAskedAbout(question);
        if (names.Count == 0)
        {
            return null;
        }

        using var timeout = new CancellationTokenSource(Deadline, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            foreach (var name in names)
            {
                var read = await speciesRange.ReadAsync(new SpeciesRangeQuery(name.Name, CountryCode: null), linked.Token)
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

                if (range.TotalRecords < MinRecords || !range.Areas.Any(area => IsoCountries.IsCountry(area.Name)))
                {
                    LogTooFewRecords(logger, name.Name, range.CanonicalName, range.TotalRecords);
                    return null;
                }

                LogFound(logger, name.Name, range.CanonicalName, range.TotalRecords);
                return SpeciesRangeExcerpt.ToHit(range, name.Name, settings.MaxAreas);
            }

            LogNotAPlant(logger, string.Join(", ", names.Select(name => name.Name)));
            return null;
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

    [LoggerMessage(LogLevel.Information,
        "The question asks where {Name} is found: offering GBIF's {CanonicalName}, {Records} record(s), before the model answers.")]
    static partial void LogFound(ILogger logger, string name, string canonicalName, long records);

    [LoggerMessage(LogLevel.Debug, "The question asks where {Names} is found, which GBIF knows as no plant.")]
    static partial void LogNotAPlant(ILogger logger, string names);

    [LoggerMessage(LogLevel.Information,
        "The question asks where {Name} is found, read as {CanonicalName} with only {Records} record(s); nothing offered.")]
    static partial void LogTooFewRecords(ILogger logger, string name, string canonicalName, long records);

    [LoggerMessage(LogLevel.Warning, "GBIF could not be read for '{Name}' before answering; the question is answered without it.")]
    static partial void LogUnavailable(ILogger logger, string name);

    [LoggerMessage(LogLevel.Warning, "Looking up '{Name}' in GBIF took over {Seconds} s; the question is answered without it.")]
    static partial void LogTimedOut(ILogger logger, string name, double seconds);

    [LoggerMessage(LogLevel.Warning, "Looking up '{Name}' in GBIF failed unexpectedly; the question is answered without it.")]
    static partial void LogFailed(ILogger logger, string name, Exception exception);
}
