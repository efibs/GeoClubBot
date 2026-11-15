using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI;
using Utilities;

namespace Infrastructure.OutputAdapters.AI.Gbif;

/// <summary>
/// Counts GBIF's records of several species in an area at once: one occurrence search for all of them
/// (<c>taxonKey</c> repeated, ORed), with no records returned (<c>limit=0</c>) and the counts read from a
/// facet over <c>taxonKey</c>. About 0.3 s for 30 species across the United States (2026-11-15).
///
/// The facet is over <c>taxonKey</c>, not <c>speciesKey</c>, because about one in thirty of the keys
/// Pl@ntNet gives are GBIF synonyms (Hesperocyparis macrocarpa for Monterey cypress): their records are
/// filed under the accepted name, and only the <c>taxonKey</c> facet counts them under the synonym's key
/// too. It also lists every ancestor (genus, family, order…), which is why its limit is generous.
///
/// Answers are kept for <see cref="AiSpeciesRangeConfiguration.CacheHours"/>, like the range reader's: a
/// follow-up on the same screenshot asks the same thing. A failure is never kept, and never thrown.
/// </summary>
public sealed partial class GbifPlantRecordCounter(
    IHttpClientFactory httpClientFactory,
    IOptions<AiToolsConfiguration> configuration,
    TimeProvider timeProvider,
    ILogger<GbifPlantRecordCounter> logger) : IPlantRecordCounter
{
    /// <summary>Facet entries asked for: every candidate, its synonyms and its ancestors, with room to spare.</summary>
    private const int FacetLimit = 1000;

    /// <summary>Cached answers at most; past it the cache starts again rather than grow without bound.</summary>
    private const int MaxCachedAnswers = 256;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<string, CachedAnswer> _cache = new(StringComparer.Ordinal);

    public async Task<Result<IReadOnlyDictionary<long, long>>> CountAsync(
        IReadOnlyCollection<long> taxonKeys,
        RecordArea area,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(taxonKeys);
        ArgumentNullException.ThrowIfNull(area);

        var keys = taxonKeys.Distinct().Order().ToList();
        if (keys.Count == 0)
        {
            return Result<IReadOnlyDictionary<long, long>>.Success(new Dictionary<long, long>());
        }

        var settings = configuration.Value.SpeciesRange;
        var filter = AreaFilter(area);
        var cacheKey = $"{filter}|{string.Join(',', keys)}";
        var now = timeProvider.GetUtcNow();

        if (_cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > now)
        {
            return Result<IReadOnlyDictionary<long, long>>.Success(cached.Counts);
        }

        try
        {
            var url = new StringBuilder(settings.BaseUrl.TrimEnd('/')).Append("/v1/occurrence/search?limit=0&occurrenceStatus=PRESENT");
            foreach (var key in keys)
            {
                url.Append("&taxonKey=").Append(key.ToString(CultureInfo.InvariantCulture));
            }

            url.Append("&facet=taxonKey&facetLimit=").Append(FacetLimit.ToString(CultureInfo.InvariantCulture)).Append(filter);

            var client = httpClientFactory.CreateClient(GbifSpeciesRangeReader.HttpClientName);
            var answer = await client.GetFromJsonAsync<OccurrenceSearchDto>(url.ToString(), Json, cancellationToken).ConfigureAwait(false);

            var byKey = new Dictionary<long, long>();
            foreach (var count in answer?.Facets?.FirstOrDefault()?.Counts ?? [])
            {
                if (long.TryParse(count.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var key) && keys.BinarySearch(key) >= 0)
                {
                    byKey[key] = count.Count;
                }
            }

            LogCounted(logger, keys.Count, filter, byKey.Count, answer?.Count ?? 0);

            if (settings.CacheHours > 0)
            {
                if (_cache.Count >= MaxCachedAnswers)
                {
                    _cache.Clear();
                }

                _cache[cacheKey] = new CachedAnswer(byKey, now + TimeSpan.FromHours(settings.CacheHours));
            }

            return Result<IReadOnlyDictionary<long, long>>.Success(byKey);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or NotSupportedException
                                              || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogUnavailable(logger, filter, exception);
            return Error.Unexpected("ai.plant_records_unavailable", "GBIF's records could not be counted.");
        }
    }

    /// <summary>The occurrence search's filter for an area: a country, a circle round a point, or a rectangle.</summary>
    internal static string AreaFilter(RecordArea area) => area switch
    {
        CountryRecordArea country => $"&country={Uri.EscapeDataString(country.Code.ToUpperInvariant())}",
        CircleRecordArea circle => string.Create(CultureInfo.InvariantCulture,
            $"&geoDistance={circle.Latitude:0.####},{circle.Longitude:0.####},{Math.Max(1, circle.RadiusKm):0.#}km"),
        BoxRecordArea box => string.Create(CultureInfo.InvariantCulture,
            $"&decimalLatitude={box.South:0.####},{box.North:0.####}&decimalLongitude={box.West:0.####},{box.East:0.####}"),
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, "An area GBIF cannot be asked about.")
    };

    private sealed record CachedAnswer(IReadOnlyDictionary<long, long> Counts, DateTimeOffset ExpiresAt);

    private sealed class OccurrenceSearchDto
    {
        [JsonPropertyName("count")]
        public long Count { get; set; }

        [JsonPropertyName("facets")]
        public List<FacetDto>? Facets { get; set; }
    }

    private sealed class FacetDto
    {
        [JsonPropertyName("counts")]
        public List<FacetCountDto>? Counts { get; set; }
    }

    private sealed class FacetCountDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("count")]
        public long Count { get; set; }
    }

    [LoggerMessage(LogLevel.Information, "Counted GBIF's records of {Keys} species ({Filter}): {Recorded} recorded there, {Total} records in all.")]
    static partial void LogCounted(ILogger logger, int keys, string filter, int recorded, long total);

    [LoggerMessage(LogLevel.Warning, "GBIF's records could not be counted ({Filter}).")]
    static partial void LogUnavailable(ILogger logger, string filter, Exception exception);
}
