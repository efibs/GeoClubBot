using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI;
using Utilities;

namespace Infrastructure.OutputAdapters.AI.Gbif;

/// <summary>
/// Reads where a plant species is recorded from GBIF's public API: no key, no allowance, and an answer
/// in well under a second when measured (2026-10-25).
///
/// Three reads. The name is matched against GBIF's backbone taxonomy first
/// (<c>/v1/species/match</c>, "Pinus kesiya" → 5285824, exactly); a common name matches only a
/// kingdom there ("khasi pine" → Plantae), so a name that does not match a genus or below is searched
/// for among the backbone's vernacular names instead (<c>/v1/species/search</c>). The species' records
/// are then counted per country — or per state or province of one country — by a facet of the
/// occurrence search, which returns the counts and no records (<c>limit=0</c>).
///
/// A wrong plant is worse than none: its countries reach the model as a cited excerpt, while "not
/// found" makes it ask again by the scientific name ("Oryza sativa" and "Cocos nucifera" match
/// exactly). So both reads take only what they can stand behind — see <see cref="IsTrusted"/> and
/// <see cref="SearchVernacularAsync"/> — and were checked against the live API on 2026-10-25 with
/// some 150 crop, tree and garden-plant names.
///
/// Answers are kept for <see cref="AiSpeciesRangeConfiguration.CacheHours"/>: the counts change slowly
/// and a conversation asks about the same plant several times. A failure is never kept, and never
/// thrown: the model is told the database could not be read and answers with what it has.
///
/// A singleton that builds its HttpClient per call from the factory, as <c>RefitChatModelClient</c>
/// does, so the cache lives as long as the process while the handlers still rotate.
/// </summary>
public sealed partial class GbifSpeciesRangeReader(
    IHttpClientFactory httpClientFactory,
    IOptions<AiToolsConfiguration> configuration,
    TimeProvider timeProvider,
    ILogger<GbifSpeciesRangeReader> logger) : ISpeciesRangeReader
{
    public const string HttpClientName = "Gbif";

    /// <summary>GBIF's own taxonomy, whose keys the occurrence search counts by.</summary>
    private const string BackboneDatasetKey = "d7dddbf4-2cf0-4f39-9b2a-bb099caae36c";

    /// <summary>Ranks a distribution means something for. Above a genus, "where is it recorded" is everywhere.</summary>
    private static readonly HashSet<string> AcceptedRanks = new(StringComparer.OrdinalIgnoreCase)
    {
        "GENUS", "SPECIES", "SUBSPECIES", "VARIETY", "FORM"
    };

    /// <summary>Cached answers at most; past it the cache starts again rather than grow without bound.</summary>
    private const int MaxCachedAnswers = 512;

    /// <summary>
    /// Species read per vernacular search. The search ranks loosely, so the plant everyone means is
    /// often far down: Cocos nucifera was 41st for "coconut", Musa ×paradisiaca 60th for "banana"
    /// (about 0.4 MB and 0.2 s for the page).
    /// </summary>
    private const int VernacularSearchSize = 100;

    /// <summary>
    /// Fewest entries naming a plant exactly as asked before it is taken for it. Sources are often
    /// indexed twice, so two entries are usually one checklist's word: "bamboo" for an agave, "poinciana"
    /// for Delonix baccal, "eucalyptus" for one hybrid. The plant meant has many: Mangifera indica 18
    /// for "mango", Hevea brasiliensis 5 for "rubber tree".
    /// </summary>
    private const int MinVernacularEntries = 3;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<string, CachedAnswer> _cache = new(StringComparer.Ordinal);

    public async Task<Result<SpeciesRange>> ReadAsync(SpeciesRangeQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var settings = configuration.Value.SpeciesRange;
        var key = $"{query.Name.Trim().ToLowerInvariant()}|{query.CountryCode?.ToUpperInvariant()}";
        var now = timeProvider.GetUtcNow();

        if (_cache.TryGetValue(key, out var cached) && cached.ExpiresAt > now)
        {
            return cached.Answer;
        }

        var answer = await ReadUncachedAsync(query, settings, cancellationToken).ConfigureAwait(false);

        // A failure to reach GBIF is not an answer about the plant, and is asked again next time.
        if ((answer.IsSuccess || answer.Error.Type == ErrorType.NotFound) && settings.CacheHours > 0)
        {
            if (_cache.Count >= MaxCachedAnswers)
            {
                _cache.Clear();
            }

            _cache[key] = new CachedAnswer(answer, now + TimeSpan.FromHours(settings.CacheHours));
        }

        return answer;
    }

    private async Task<Result<SpeciesRange>> ReadUncachedAsync(
        SpeciesRangeQuery query,
        AiSpeciesRangeConfiguration settings,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            var baseUrl = settings.BaseUrl.TrimEnd('/');

            var name = query.Name.Trim();
            var taxon = await MatchAsync(client, baseUrl, name, cancellationToken).ConfigureAwait(false)
                        ?? await SearchVernacularAsync(client, baseUrl, name, cancellationToken).ConfigureAwait(false);

            if (taxon is null)
            {
                LogNotFound(logger, query.Name);
                return Error.NotFound("ai.species_not_found", $"No plant called '{query.Name}' is known to GBIF.");
            }

            var facet = query.CountryCode is null ? "country" : "stateProvince";
            var url = $"{baseUrl}/v1/occurrence/search?taxonKey={taxon.Key.ToString(CultureInfo.InvariantCulture)}"
                      + $"&facet={facet}&facetLimit={Math.Clamp(settings.MaxAreas + 5, 1, 50).ToString(CultureInfo.InvariantCulture)}"
                      + "&limit=0&occurrenceStatus=PRESENT"
                      + (query.CountryCode is null ? string.Empty : $"&country={Uri.EscapeDataString(query.CountryCode.ToUpperInvariant())}");

            var occurrences = await client.GetFromJsonAsync<OccurrenceSearchDto>(url, Json, cancellationToken).ConfigureAwait(false);

            var areas = occurrences?.Facets?
                .FirstOrDefault()?.Counts?
                .Where(count => !string.IsNullOrWhiteSpace(count.Name))
                .Select(count => new SpeciesRangeArea(count.Name!, count.Count))
                .ToList() ?? [];

            var range = new SpeciesRange(
                taxon.Key,
                taxon.ScientificName,
                taxon.CanonicalName,
                taxon.Rank,
                taxon.ByCommonName,
                taxon.Fuzzy,
                query.CountryCode?.ToUpperInvariant(),
                areas,
                occurrences?.Count ?? 0);

            LogRead(logger, query.Name, range.CanonicalName, range.TaxonKey, range.CountryCode ?? "every country", areas.Count, range.TotalRecords);
            return range;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or NotSupportedException
                                              || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogUnavailable(logger, query.Name, exception);
            return Error.Unexpected("ai.species_range_unavailable", "The species database could not be reached.");
        }
    }

    /// <summary>The backbone's match for a name, or null when it is not one to stand behind.</summary>
    private static async Task<Taxon?> MatchAsync(HttpClient client, string baseUrl, string name, CancellationToken cancellationToken)
    {
        var asked = Capitalised(name);

        // The kingdom is a hint, not a filter: it settles a name shared by a plant and an animal.
        var match = await client.GetFromJsonAsync<SpeciesMatchDto>(
                $"{baseUrl}/v1/species/match?name={Uri.EscapeDataString(asked)}&kingdom=Plantae", Json, cancellationToken)
            .ConfigureAwait(false);

        if (match?.UsageKey is not { } usageKey
            || !AcceptedRanks.Contains(match.Rank ?? string.Empty)
            || !IsTrusted(match, asked))
        {
            return null;
        }

        // A synonym's records are filed under the name it stands for.
        return new Taxon(
            match.AcceptedUsageKey ?? usageKey,
            match.ScientificName ?? asked,
            match.CanonicalName ?? match.ScientificName ?? asked,
            match.Rank!,
            ByCommonName: false,
            Fuzzy: match.MatchType == "FUZZY");
    }

    /// <summary>
    /// The name with its first letter upper case. GBIF reads one word as a genus only when it is
    /// capitalised: "yucca", "pinus" and "eucalyptus" match the kingdom, "Yucca" the genus. A binomial
    /// matches either way ("cocos nucifera" is exact).
    /// </summary>
    private static string Capitalised(string name) =>
        name.Length > 0 && char.IsLower(name[0])
            ? string.Concat(char.ToUpperInvariant(name[0]).ToString(), name.AsSpan(1))
            : name;

    /// <summary>Whether a match at a usable rank is the plant asked for.</summary>
    private static bool IsTrusted(SpeciesMatchDto match, string asked) => match.MatchType switch
    {
        "EXACT" => true,

        // A placeholder species in the backbone ("Agave spec") turns a plain genus into a higher-rank
        // match. It is still the genus asked for when the names agree; "Coca" read as Cota is not.
        "HIGHERRANK" => string.Equals(match.CanonicalName, asked, StringComparison.OrdinalIgnoreCase),

        // A misspelt binomial is worth reading ("Pinus kesia" → Pinus kesiya). One word read
        // approximately is mostly a common name read as a genus: "Banana" → Banara, "Neem" → Neea,
        // "Mango" → Manga. Those go to the vernacular names instead.
        "FUZZY" => asked.Any(char.IsWhiteSpace),

        _ => false
    };

    /// <summary>
    /// The plant of the backbone that goes by this common name, or null when none clearly does.
    /// Searching every checklist instead finds the same tree a dozen times under other datasets' keys.
    ///
    /// The search matches words, not names — "rice" finds Acacia riceana ("Rice's wattle") and rice
    /// viruses — so a plant counts only for its vernacular names that are the name asked, and a plant is
    /// taken only when it is called so more often than any other. "Rubber tree" is also a name of
    /// Cerbera inflata, Castilla elastica, Ficus citrifolia and Euphorbia tirucalli, twice each; Hevea
    /// brasiliensis five times. A common name several plants share equally ("flame tree") is not
    /// guessed at, and nothing but a plant is ever taken.
    /// </summary>
    private static async Task<Taxon?> SearchVernacularAsync(HttpClient client, string baseUrl, string name, CancellationToken cancellationToken)
    {
        var search = await client.GetFromJsonAsync<SpeciesSearchDto>(
                $"{baseUrl}/v1/species/search?q={Uri.EscapeDataString(name)}&qField=VERNACULAR&rank=SPECIES"
                + $"&datasetKey={BackboneDatasetKey}&limit={VernacularSearchSize.ToString(CultureInfo.InvariantCulture)}",
                Json, cancellationToken)
            .ConfigureAwait(false);

        var asked = VernacularForm(name);

        // A synonym is counted with the species it stands for: one plant, however it is filed.
        var candidates = (search?.Results ?? [])
            .Where(result => result.Key is not null
                             && string.Equals(result.Kingdom, "Plantae", StringComparison.OrdinalIgnoreCase))
            .Select(result => (Result: result, Entries: result.VernacularNames?
                .Count(vernacular => VernacularForm(vernacular.VernacularName) == asked) ?? 0))
            .Where(candidate => candidate.Entries > 0)
            .GroupBy(candidate => KeyOf(candidate.Result))
            .Select(plant => (
                Key: plant.Key,
                Result: plant.MaxBy(candidate => candidate.Entries).Result,
                Entries: plant.Sum(candidate => candidate.Entries)))
            .OrderByDescending(plant => plant.Entries)
            .ToList();

        if (candidates.Count == 0
            || candidates[0].Entries < MinVernacularEntries
            || (candidates.Count > 1 && candidates[1].Entries == candidates[0].Entries))
        {
            return null;
        }

        var found = candidates[0].Result;
        return new Taxon(
            candidates[0].Key,
            found.ScientificName ?? found.CanonicalName ?? name,
            found.CanonicalName ?? found.ScientificName ?? name,
            found.Rank ?? "SPECIES",
            ByCommonName: true,
            Fuzzy: false);
    }

    /// <summary>The key a search result's records are counted under: a synonym's is its accepted name's.</summary>
    private static long KeyOf(SpeciesSearchResultDto result) => result.AcceptedKey ?? result.NubKey ?? result.Key!.Value;

    /// <summary>A common name as compared: case, hyphens and spacing aside ("Joshua-tree" is "joshua tree").</summary>
    private static string VernacularForm(string? name) =>
        string.Join(' ', (name ?? string.Empty).Replace('-', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();

    private sealed record Taxon(long Key, string ScientificName, string CanonicalName, string Rank, bool ByCommonName, bool Fuzzy);

    private sealed record CachedAnswer(Result<SpeciesRange> Answer, DateTimeOffset ExpiresAt);

    private sealed class SpeciesMatchDto
    {
        [JsonPropertyName("usageKey")]
        public long? UsageKey { get; set; }

        [JsonPropertyName("acceptedUsageKey")]
        public long? AcceptedUsageKey { get; set; }

        [JsonPropertyName("scientificName")]
        public string? ScientificName { get; set; }

        [JsonPropertyName("canonicalName")]
        public string? CanonicalName { get; set; }

        [JsonPropertyName("rank")]
        public string? Rank { get; set; }

        [JsonPropertyName("matchType")]
        public string? MatchType { get; set; }
    }

    private sealed class SpeciesSearchDto
    {
        [JsonPropertyName("results")]
        public List<SpeciesSearchResultDto>? Results { get; set; }
    }

    private sealed class SpeciesSearchResultDto
    {
        [JsonPropertyName("key")]
        public long? Key { get; set; }

        [JsonPropertyName("nubKey")]
        public long? NubKey { get; set; }

        [JsonPropertyName("acceptedKey")]
        public long? AcceptedKey { get; set; }

        [JsonPropertyName("scientificName")]
        public string? ScientificName { get; set; }

        [JsonPropertyName("canonicalName")]
        public string? CanonicalName { get; set; }

        [JsonPropertyName("rank")]
        public string? Rank { get; set; }

        [JsonPropertyName("kingdom")]
        public string? Kingdom { get; set; }

        [JsonPropertyName("vernacularNames")]
        public List<VernacularNameDto>? VernacularNames { get; set; }
    }

    private sealed class VernacularNameDto
    {
        [JsonPropertyName("vernacularName")]
        public string? VernacularName { get; set; }
    }

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

    [LoggerMessage(LogLevel.Debug,
        "GBIF: '{Name}' is {CanonicalName} ({TaxonKey}); {AreaCount} area(s) in {Scope}, {Records} record(s).")]
    static partial void LogRead(ILogger logger, string name, string canonicalName, long taxonKey, string scope, int areaCount, long records);

    [LoggerMessage(LogLevel.Information, "GBIF has no plant it can stand behind for '{Name}'; the model was told to use the scientific name.")]
    static partial void LogNotFound(ILogger logger, string name);

    [LoggerMessage(LogLevel.Warning, "GBIF could not be read for '{Name}'; the model was told so.")]
    static partial void LogUnavailable(ILogger logger, string name, Exception exception);
}
