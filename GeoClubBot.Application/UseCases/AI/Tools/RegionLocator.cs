using Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Retrieval;

namespace UseCases.UseCases.AI.Tools;

/// <param name="ImageUrl">The map, stored in the image relay.</param>
/// <param name="CountryName">The country shown.</param>
/// <param name="Regions">The regions highlighted, as labelled on the map.</param>
/// <param name="Title">What the map's embed is titled.</param>
public sealed record RegionMapResult(string ImageUrl, string CountryName, IReadOnlyList<string> Regions, string Title);

/// <summary>
/// Draws the regions an answer names onto a map of their country, after the answer is written, so a
/// player who does not know where "Lạng Sơn and Quảng Ninh" are sees it.
///
/// The country is one the answer or the question names (the guides' gazetteer, as retrieval uses it);
/// its regions come from the boundary dataset, and the answer's text is read for their names. The
/// country with the most regions named wins; one map an answer. Costs no AI request and never fails
/// the answer: whatever goes wrong, the answer goes out without a map.
/// </summary>
public sealed partial class RegionLocator(
    IRegionBoundaries boundaries,
    IRegionMapDrawer drawer,
    KnowledgeCountryDirectory countryDirectory,
    IOptions<AiToolsConfiguration> toolsConfiguration,
    TimeProvider timeProvider,
    ILogger<RegionLocator> logger)
{
    /// <summary>Countries read for their regions, most mentioned first. An answer comparing many countries gets no map.</summary>
    private const int MaxCountriesTried = 3;

    /// <summary>The map for <paramref name="answer"/>, or null when it names no regions of one country, or none could be drawn.</summary>
    public async Task<RegionMapResult?> LocateAsync(string? answer, string? question, CancellationToken cancellationToken)
    {
        var settings = toolsConfiguration.Value.RegionMap;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(answer))
        {
            return null;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds)), timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            var gazetteer = await countryDirectory.ReadAsync(linked.Token).ConfigureAwait(false);

            // The answer's countries first: a question about Malaysia answered with Thailand's regions is about Thailand.
            var codes = gazetteer.FindMentions(answer).Concat(gazetteer.FindMentions(question))
                .Select(mention => IsoCountries.Resolve(mention.Name, gazetteer))
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxCountriesTried)
                .ToList();

            if (codes.Count == 0)
            {
                return null;
            }

            string? bestCode = null;
            IReadOnlyList<RegionName> bestRegions = [];
            foreach (var code in codes)
            {
                var names = await boundaries.ReadNamesAsync(code, linked.Token).ConfigureAwait(false);
                var found = RegionMention.Find(answer, names);
                if (found.Count > bestRegions.Count)
                {
                    bestCode = code;
                    bestRegions = found;
                }
            }

            if (bestCode is null || bestRegions.Count == 0)
            {
                return null;
            }

            var shown = bestRegions.Take(Math.Max(1, settings.MaxRegions)).ToList();
            var countryName = IsoCountries.NameOf(bestCode);
            var title = RegionMapExcerpt.Title(countryName, [.. shown.Select(region => region.Name)]);

            var url = await drawer.DrawAsync(
                    new RegionMapRequest(bestCode, [.. shown.Select(region => new RegionHighlight(region.Id, region.Name, 1d))], title),
                    linked.Token)
                .ConfigureAwait(false);

            if (url is null)
            {
                return null;
            }

            LogDrawn(logger, shown.Count, countryName);
            return new RegionMapResult(url, countryName, [.. shown.Select(region => region.Name)], title);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogTimedOut(logger, settings.TimeoutSeconds);
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(logger, exception);
            return null;
        }
    }

    [LoggerMessage(LogLevel.Information, "Drew a map of the {Count} region(s) of {Country} the answer names.")]
    static partial void LogDrawn(ILogger logger, int count, string country);

    [LoggerMessage(LogLevel.Warning, "The region map took over {Seconds} s; the answer goes out without it.")]
    static partial void LogTimedOut(ILogger logger, int seconds);

    [LoggerMessage(LogLevel.Warning, "The region map failed unexpectedly; the answer goes out without it.")]
    static partial void LogFailed(ILogger logger, Exception exception);
}
