using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI;

namespace Infrastructure.OutputAdapters.AI.Regions;

/// <summary>
/// The towns a member may name as where a screenshot was taken — GeoNames' towns of 15,000 people or
/// more (CC BY 4.0, about 34,000) — and the states and provinces of Natural Earth's admin-1 boundaries,
/// which the region maps read already. Each town is tied to its state through GeoNames' admin-1 key, so
/// "Quincy, MA" is told from Quincy, Illinois.
///
/// The towns' file (about 3 MB zipped) is downloaded once into the data directory beside the
/// boundaries, and both are read on a background task at start-up; a question asked before that is
/// done goes without its place rather than wait. A town's other names (Bombay, Saigon, Kiev) are kept
/// for towns of half a million or more only: smaller towns' lists are long and mostly other languages'
/// spellings, which match more words than places.
/// </summary>
public sealed partial class GeoNamesPlaceDirectory(
    IHttpClientFactory httpClientFactory,
    NaturalEarthRegionBoundaries boundaries,
    IOptions<AiToolsConfiguration> configuration,
    ILogger<GeoNamesPlaceDirectory> logger) : IPlaceDirectory
{
    public const string HttpClientName = "GeoNames";

    /// <summary>Population from which a town's other names are kept.</summary>
    private const long AliasPopulation = 500_000;

    private readonly Lock _loadLock = new();
    private Task<IReadOnlyList<KnownPlace>>? _loading;

    /// <summary>Starts reading the places, if it has not started; returns at once.</summary>
    public void Warm() => _ = LoadAsync();

    public async Task<IReadOnlyList<KnownPlace>> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!configuration.Value.PlantIdentification.Location.Enabled)
        {
            return [];
        }

        try
        {
            return await LoadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The caller's deadline, not ours: the places keep loading for the next question.
            return [];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogUnavailable(logger, exception);
            return [];
        }
    }

    private Task<IReadOnlyList<KnownPlace>> LoadAsync()
    {
        lock (_loadLock)
        {
            // A failed load is forgotten, so the next reader tries again rather than failing for ever.
            if (_loading is null || _loading.IsFaulted || _loading.IsCanceled)
            {
                _loading = Task.Run(ReadPlacesAsync);
            }

            return _loading;
        }
    }

    private async Task<IReadOnlyList<KnownPlace>> ReadPlacesAsync()
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var regionsRead = boundaries.ReadEveryRegionAsync(CancellationToken.None);
        var towns = await ReadTownRowsAsync().ConfigureAwait(false);
        var regions = await regionsRead.ConfigureAwait(false);

        var places = new List<KnownPlace>(towns.Count + 5000);
        var regionsByKey = new Dictionary<string, RegionShape>(StringComparer.Ordinal);

        foreach (var (code, shapes) in regions)
        {
            foreach (var shape in shapes)
            {
                if (shape.GeoNamesKey is { } key)
                {
                    regionsByKey.TryAdd(key, shape);
                }

                var bounds = shape.Bounds;
                places.Add(new KnownPlace(
                    KnownPlaceKind.Region,
                    shape.Name,
                    shape.Aliases,
                    code,
                    shape.GeoNamesKey,
                    RegionName: null,
                    shape.Postal is { } postal ? [postal] : [],
                    shape.LabelLatitude,
                    shape.LabelLongitude,
                    Population: 0,
                    new PlaceBounds(bounds.West, bounds.South, bounds.East, bounds.North)));
            }
        }

        foreach (var town in towns)
        {
            // Natural Earth keeps some countries' GeoNames codes from before a reform: a key whose region
            // does not hold the town is someone else's, and the town is left without one.
            var key = string.IsNullOrWhiteSpace(town.Admin1) ? null : $"{town.CountryCode}.{town.Admin1}";
            var region = key is not null && regionsByKey.TryGetValue(key, out var found) && Holds(found, town) ? found : null;
            places.Add(new KnownPlace(
                KnownPlaceKind.Town,
                town.Name,
                town.Aliases,
                town.CountryCode,
                region is null ? null : key,
                region?.Name,
                [],
                town.Latitude,
                town.Longitude,
                town.Population,
                Bounds: null));
        }

        LogLoaded(logger, towns.Count, places.Count - towns.Count, System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalSeconds);
        return places;
    }

    /// <summary>Whether a town lies within its region's rectangle, give or take a few kilometres of coast.</summary>
    private static bool Holds(RegionShape region, TownRow town)
    {
        const double Margin = 0.1;
        var (west, south, east, north) = region.Bounds;
        return town.Latitude >= south - Margin && town.Latitude <= north + Margin
               && town.Longitude >= west - Margin && town.Longitude <= east + Margin;
    }

    /// <summary>The towns of the GeoNames file, downloading it first when it is not there.</summary>
    private async Task<IReadOnlyList<TownRow>> ReadTownRowsAsync()
    {
        var settings = configuration.Value;
        var url = settings.PlantIdentification.Location.TownsUrl;
        var path = Path.Combine(settings.RegionMap.DataDirectory, Path.GetFileName(new Uri(url).AbsolutePath));

        if (!File.Exists(path))
        {
            await DownloadAsync(url, path, settings.PlantIdentification.Location.DownloadTimeoutSeconds).ConfigureAwait(false);
        }

        await using var file = File.OpenRead(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);
        var entry = archive.Entries.FirstOrDefault(candidate => candidate.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException($"{path} holds no towns file.");

        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return ParseTowns(reader);
    }

    /// <summary>
    /// GeoNames' tab-separated towns: id, name, ASCII name, other names (comma-separated), latitude,
    /// longitude, feature class and code, country, other countries, admin-1 to admin-4, population, …
    /// A malformed row is skipped.
    /// </summary>
    internal static IReadOnlyList<TownRow> ParseTowns(TextReader reader)
    {
        var towns = new List<TownRow>();
        while (reader.ReadLine() is { } line)
        {
            var fields = line.Split('\t');
            if (fields.Length < 15
                || string.IsNullOrWhiteSpace(fields[1])
                || fields[8].Length != 2
                || !double.TryParse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
                || !double.TryParse(fields[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
            {
                continue;
            }

            _ = long.TryParse(fields[14], NumberStyles.Integer, CultureInfo.InvariantCulture, out var population);

            var aliases = new List<string>();
            if (!string.IsNullOrWhiteSpace(fields[2]) && !string.Equals(fields[2], fields[1], StringComparison.Ordinal))
            {
                aliases.Add(fields[2].Trim());
            }

            if (population >= AliasPopulation)
            {
                aliases.AddRange(fields[3].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(IsUsableAlias)
                    .Where(alias => !string.Equals(alias, fields[1], StringComparison.OrdinalIgnoreCase)));
            }

            towns.Add(new TownRow(
                fields[1].Trim(),
                aliases.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                fields[8].ToUpperInvariant(),
                fields[10].Trim(),
                latitude,
                longitude,
                population));
        }

        return towns;
    }

    /// <summary>An other name worth matching: in Latin letters, four or more of them, not a code in capitals, at most four words.</summary>
    private static bool IsUsableAlias(string alias) =>
        alias.Length >= 4
        && LatinName().IsMatch(alias)
        && !alias.All(character => !char.IsLetter(character) || char.IsUpper(character))
        && alias.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 4;

    private async Task DownloadAsync(string url, string path, int timeoutSeconds)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = $"{path}.download";

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        var client = httpClientFactory.CreateClient(HttpClientName);

        LogDownloading(logger, url);
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using (var file = File.Create(temporary))
        await using (var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
        {
            await body.CopyToAsync(file, timeout.Token).ConfigureAwait(false);
        }

        File.Move(temporary, path, overwrite: true);
        LogDownloaded(logger, new FileInfo(path).Length);
    }

    /// <param name="Admin1">GeoNames' admin-1 code within the country: "MA" in the United States, "58" in Bulgaria.</param>
    internal sealed record TownRow(
        string Name,
        IReadOnlyList<string> Aliases,
        string CountryCode,
        string Admin1,
        double Latitude,
        double Longitude,
        long Population);

    [GeneratedRegex(@"^[\p{IsBasicLatin}\p{IsLatin-1Supplement}\p{IsLatinExtended-A}\p{IsLatinExtended-B}\p{IsLatinExtendedAdditional}]+$", RegexOptions.CultureInvariant)]
    private static partial Regex LatinName();

    [LoggerMessage(LogLevel.Information, "Downloading GeoNames' towns from {Url}; this happens once.")]
    static partial void LogDownloading(ILogger logger, string url);

    [LoggerMessage(LogLevel.Information, "Downloaded GeoNames' towns ({Bytes} bytes).")]
    static partial void LogDownloaded(ILogger logger, long bytes);

    [LoggerMessage(LogLevel.Information, "Read {Towns} towns and {Regions} regions for placing screenshots in {Seconds:F1} s.")]
    static partial void LogLoaded(ILogger logger, int towns, int regions, double seconds);

    [LoggerMessage(LogLevel.Warning, "The towns could not be read; plants are identified without where the screenshot was taken until the next try.")]
    static partial void LogUnavailable(ILogger logger, Exception exception);
}
