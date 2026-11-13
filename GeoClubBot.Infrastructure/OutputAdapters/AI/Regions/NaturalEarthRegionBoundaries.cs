using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI;

namespace Infrastructure.OutputAdapters.AI.Regions;

/// <summary>One region's outline: its rings, each a run of longitude/latitude pairs, the first the outer edge.</summary>
internal sealed record RegionShape(string Id, string Name, IReadOnlyList<string> Aliases, IReadOnlyList<IReadOnlyList<(double Longitude, double Latitude)>> Rings, double LabelLongitude, double LabelLatitude)
{
    /// <summary>The rectangle around every ring, so a point far from the region is turned away without walking its rings.</summary>
    public (double West, double South, double East, double North) Bounds { get; } = BoundsOf(Rings);

    /// <summary>Whether the point lies inside the region: the even-odd rule over its rings, holes included.</summary>
    public bool Contains(double longitude, double latitude)
    {
        if (longitude < Bounds.West || longitude > Bounds.East || latitude < Bounds.South || latitude > Bounds.North)
        {
            return false;
        }

        var inside = false;
        foreach (var ring in Rings)
        {
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                var (xi, yi) = ring[i];
                var (xj, yj) = ring[j];
                if (yi > latitude != yj > latitude && longitude < (xj - xi) * (latitude - yi) / (yj - yi) + xi)
                {
                    inside = !inside;
                }
            }
        }

        return inside;
    }

    /// <summary>
    /// How far the point lies from the region's edge, in degrees of latitude (longitude scaled to match
    /// at the point's latitude); <see cref="double.PositiveInfinity"/> when it is further than
    /// <paramref name="reach"/> from the region's rectangle.
    /// </summary>
    public double DistanceTo(double longitude, double latitude, double reach)
    {
        var scale = Math.Max(0.01, Math.Cos(latitude * Math.PI / 180));
        if (longitude < Bounds.West - reach / scale || longitude > Bounds.East + reach / scale
            || latitude < Bounds.South - reach || latitude > Bounds.North + reach)
        {
            return double.PositiveInfinity;
        }

        var nearest = double.PositiveInfinity;
        foreach (var ring in Rings)
        {
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                var ax = (ring[j].Longitude - longitude) * scale;
                var ay = ring[j].Latitude - latitude;
                var bx = (ring[i].Longitude - longitude) * scale;
                var by = ring[i].Latitude - latitude;
                var dx = bx - ax;
                var dy = by - ay;
                var length = (dx * dx) + (dy * dy);
                var t = length == 0 ? 0 : Math.Clamp(-((ax * dx) + (ay * dy)) / length, 0, 1);
                var px = ax + (t * dx);
                var py = ay + (t * dy);
                nearest = Math.Min(nearest, Math.Sqrt((px * px) + (py * py)));
            }
        }

        return nearest;
    }

    private static (double, double, double, double) BoundsOf(IReadOnlyList<IReadOnlyList<(double Longitude, double Latitude)>> rings)
    {
        double west = double.MaxValue, south = double.MaxValue, east = double.MinValue, north = double.MinValue;
        foreach (var (longitude, latitude) in rings.SelectMany(ring => ring))
        {
            west = Math.Min(west, longitude);
            east = Math.Max(east, longitude);
            south = Math.Min(south, latitude);
            north = Math.Max(north, latitude);
        }

        return (west, south, east, north);
    }
}

/// <summary>
/// Natural Earth's admin-1 states and provinces (public domain), read from the GeoJSON the project
/// publishes: the names of each country's regions and their outlines, simplified for drawing.
///
/// The file is downloaded once into the data directory and kept; a missing file is fetched again. It
/// is read on the first use, or at start-up when the warm-up asks, on a background task that the
/// readers wait for only as long as their own deadline allows: an answer is never held for the file.
/// </summary>
public sealed partial class NaturalEarthRegionBoundaries(
    IHttpClientFactory httpClientFactory,
    IOptions<AiToolsConfiguration> configuration,
    ILogger<NaturalEarthRegionBoundaries> logger) : IRegionBoundaries
{
    public const string HttpClientName = "NaturalEarth";

    public const string FileName = "ne_10m_admin_1_states_provinces.geojson";

    /// <summary>
    /// How far off every outline a point may lie and still be placed in the nearest region, in degrees
    /// (about 11 km): the coast the outlines miss, not the open sea a guess may land in.
    /// </summary>
    internal const double CoastReachDegrees = 0.1;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Lock _loadLock = new();
    private Task<IReadOnlyDictionary<string, IReadOnlyList<RegionShape>>>? _loading;

    /// <summary>Starts reading the file, if it has not started; returns at once.</summary>
    public void Warm() => _ = LoadAsync();

    public async Task<IReadOnlyList<RegionName>> ReadNamesAsync(string countryCode, CancellationToken cancellationToken = default)
    {
        var shapes = await ReadShapesAsync(countryCode, cancellationToken).ConfigureAwait(false);
        return [.. shapes.Select(shape => new RegionName(shape.Id, shape.Name, shape.Aliases))];
    }

    public async Task<RegionPlace?> LocateAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        if (!configuration.Value.RegionMap.Enabled || double.IsNaN(latitude) || double.IsNaN(longitude))
        {
            return null;
        }

        try
        {
            var byCountry = await LoadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            foreach (var (code, shapes) in byCountry)
            {
                foreach (var shape in shapes)
                {
                    if (shape.Contains(longitude, latitude))
                    {
                        return new RegionPlace(code, shape.Name);
                    }
                }
            }

            // Street View stands on the shore, and the outlines are drawn coarser than that: a round on
            // Sri Lanka's Mannar Island lay 2 km off them (2026-11-13). Within reach, the nearest region.
            RegionPlace? nearest = null;
            var nearestDistance = CoastReachDegrees;
            foreach (var (code, shapes) in byCountry)
            {
                foreach (var shape in shapes)
                {
                    var distance = shape.DistanceTo(longitude, latitude, CoastReachDegrees);
                    if (distance <= nearestDistance)
                    {
                        nearestDistance = distance;
                        nearest = new RegionPlace(code, shape.Name);
                    }
                }
            }

            return nearest;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogUnavailable(logger, exception);
            return null;
        }
    }

    /// <summary>A country's regions with their outlines; empty when unknown, or when the file is not read yet.</summary>
    internal async Task<IReadOnlyList<RegionShape>> ReadShapesAsync(string countryCode, CancellationToken cancellationToken)
    {
        if (!configuration.Value.RegionMap.Enabled || string.IsNullOrWhiteSpace(countryCode))
        {
            return [];
        }

        try
        {
            var byCountry = await LoadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            return byCountry.TryGetValue(countryCode.ToUpperInvariant(), out var shapes) ? shapes : [];
        }
        catch (OperationCanceledException)
        {
            // The caller's deadline, not ours: the file keeps loading for the next answer.
            return [];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogUnavailable(logger, exception);
            return [];
        }
    }

    private Task<IReadOnlyDictionary<string, IReadOnlyList<RegionShape>>> LoadAsync()
    {
        lock (_loadLock)
        {
            // A failed load is forgotten, so the next reader tries again rather than failing for ever.
            if (_loading is null || _loading.IsFaulted || _loading.IsCanceled)
            {
                _loading = Task.Run(ReadFileAsync);
            }

            return _loading;
        }
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<RegionShape>>> ReadFileAsync()
    {
        var settings = configuration.Value.RegionMap;
        var path = Path.Combine(settings.DataDirectory, FileName);

        if (!File.Exists(path))
        {
            await DownloadAsync(settings, path).ConfigureAwait(false);
        }

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        FeatureCollectionDto? collection;
        await using (var stream = File.OpenRead(path))
        {
            collection = await JsonSerializer.DeserializeAsync<FeatureCollectionDto>(stream, Json).ConfigureAwait(false);
        }

        var byCountry = new Dictionary<string, List<RegionShape>>(StringComparer.Ordinal);
        var points = 0;
        foreach (var feature in collection?.Features ?? [])
        {
            if (ToShape(feature, settings.SimplifyToleranceDegrees) is not { } shape || CountryCodeOf(feature.Properties) is not { } code)
            {
                continue;
            }

            points += shape.Rings.Sum(ring => ring.Count);
            if (!byCountry.TryGetValue(code, out var list))
            {
                byCountry[code] = list = [];
            }

            list.Add(shape);
        }

        LogLoaded(logger, byCountry.Count, byCountry.Values.Sum(list => list.Count), points, System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalSeconds);

        return byCountry.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<RegionShape>)entry.Value, StringComparer.Ordinal);
    }

    private async Task DownloadAsync(AiRegionMapConfiguration settings, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.download";

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.DownloadTimeoutSeconds));
        var client = httpClientFactory.CreateClient(HttpClientName);

        LogDownloading(logger, settings.BoundariesUrl);
        using var response = await client.GetAsync(settings.BoundariesUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using (var file = File.Create(temporary))
        await using (var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
        {
            await body.CopyToAsync(file, timeout.Token).ConfigureAwait(false);
        }

        File.Move(temporary, path, overwrite: true);
        LogDownloaded(logger, new FileInfo(path).Length);
    }

    /// <summary>
    /// The country a feature belongs to: its ISO alpha-2, or the prefix of its ISO 3166-2 code where
    /// Natural Earth writes "-1" (a few disputed and dependent places).
    /// </summary>
    internal static string? CountryCodeOf(PropertiesDto? properties)
    {
        if (properties is null)
        {
            return null;
        }

        if (properties.IsoA2 is { Length: 2 } iso && iso != "-1" && char.IsLetter(iso[0]) && char.IsLetter(iso[1]))
        {
            return iso.ToUpperInvariant();
        }

        if (properties.Iso31662 is { Length: >= 3 } code && code[2] == '-' && char.IsLetter(code[0]) && char.IsLetter(code[1]))
        {
            return code[..2].ToUpperInvariant();
        }

        return null;
    }

    internal static RegionShape? ToShape(FeatureDto feature, double tolerance)
    {
        var properties = feature.Properties;
        if (properties is null || feature.Geometry is null)
        {
            return null;
        }

        var name = FirstNonEmpty(properties.NameEn, properties.Name, properties.GnName, properties.WoeName);
        if (name is null)
        {
            return null;
        }

        var aliases = new[] { properties.Name, properties.GnName, properties.WoeName, properties.NameLocal }
            .Concat((properties.NameAlt ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(alias => !string.IsNullOrWhiteSpace(alias) && !string.Equals(alias, name, StringComparison.OrdinalIgnoreCase))
            .Select(alias => alias!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rings = ReadRings(feature.Geometry)
            .Select(ring => Simplify(ring, tolerance))
            .Where(ring => ring.Count >= 3)
            .ToList();

        if (rings.Count == 0)
        {
            return null;
        }

        var id = FirstNonEmpty(properties.Adm1Code, properties.NeId?.ToString(System.Globalization.CultureInfo.InvariantCulture), properties.Iso31662) ?? name;

        // Natural Earth's label point, or the centroid of the largest ring when it has none.
        var (labelLongitude, labelLatitude) = properties is { Longitude: { } lon, Latitude: { } lat }
            ? (lon, lat)
            : Centroid(rings.MaxBy(ring => ring.Count)!);

        return new RegionShape(id, name.Trim(), aliases, rings, labelLongitude, labelLatitude);
    }

    /// <summary>Every ring of a polygon or multipolygon, holes included: a hole is drawn by the even-odd rule.</summary>
    private static IEnumerable<IReadOnlyList<(double, double)>> ReadRings(GeometryDto geometry)
    {
        if (geometry.Coordinates.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        switch (geometry.Type)
        {
            case "Polygon":
                foreach (var ring in geometry.Coordinates.EnumerateArray())
                {
                    yield return ReadRing(ring);
                }

                break;

            case "MultiPolygon":
                foreach (var polygon in geometry.Coordinates.EnumerateArray())
                {
                    foreach (var ring in polygon.EnumerateArray())
                    {
                        yield return ReadRing(ring);
                    }
                }

                break;
        }
    }

    private static List<(double, double)> ReadRing(JsonElement ring)
    {
        var points = new List<(double, double)>();
        foreach (var point in ring.EnumerateArray())
        {
            if (point.ValueKind == JsonValueKind.Array && point.GetArrayLength() >= 2
                && point[0].TryGetDouble(out var longitude) && point[1].TryGetDouble(out var latitude))
            {
                points.Add((longitude, latitude));
            }
        }

        return points;
    }

    /// <summary>Douglas–Peucker: the ring with every point closer than <paramref name="tolerance"/> to the line between its neighbours dropped.</summary>
    internal static IReadOnlyList<(double Longitude, double Latitude)> Simplify(IReadOnlyList<(double Longitude, double Latitude)> ring, double tolerance)
    {
        if (tolerance <= 0 || ring.Count <= 4)
        {
            return ring;
        }

        var keep = new bool[ring.Count];
        keep[0] = keep[^1] = true;
        var stack = new Stack<(int First, int Last)>();
        stack.Push((0, ring.Count - 1));

        while (stack.Count > 0)
        {
            var (first, last) = stack.Pop();
            var farthest = -1;
            var farthestDistance = 0d;

            for (var index = first + 1; index < last; index++)
            {
                var distance = DistanceToSegment(ring[index], ring[first], ring[last]);
                if (distance > farthestDistance)
                {
                    farthestDistance = distance;
                    farthest = index;
                }
            }

            if (farthest >= 0 && farthestDistance > tolerance)
            {
                keep[farthest] = true;
                stack.Push((first, farthest));
                stack.Push((farthest, last));
            }
        }

        var simplified = new List<(double, double)>();
        for (var index = 0; index < ring.Count; index++)
        {
            if (keep[index])
            {
                simplified.Add(ring[index]);
            }
        }

        return simplified;
    }

    private static double DistanceToSegment((double X, double Y) point, (double X, double Y) a, (double X, double Y) b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared == 0)
        {
            return Math.Sqrt((point.X - a.X) * (point.X - a.X) + (point.Y - a.Y) * (point.Y - a.Y));
        }

        var t = Math.Clamp(((point.X - a.X) * dx + (point.Y - a.Y) * dy) / lengthSquared, 0, 1);
        var projectedX = a.X + t * dx;
        var projectedY = a.Y + t * dy;
        return Math.Sqrt((point.X - projectedX) * (point.X - projectedX) + (point.Y - projectedY) * (point.Y - projectedY));
    }

    private static (double, double) Centroid(IReadOnlyList<(double Longitude, double Latitude)> ring) =>
        (ring.Average(point => point.Longitude), ring.Average(point => point.Latitude));

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    internal sealed class FeatureCollectionDto
    {
        [JsonPropertyName("features")]
        public List<FeatureDto>? Features { get; set; }
    }

    internal sealed class FeatureDto
    {
        [JsonPropertyName("properties")]
        public PropertiesDto? Properties { get; set; }

        [JsonPropertyName("geometry")]
        public GeometryDto? Geometry { get; set; }
    }

    internal sealed class GeometryDto
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("coordinates")]
        public JsonElement Coordinates { get; set; }
    }

    internal sealed class PropertiesDto
    {
        [JsonPropertyName("iso_a2")]
        public string? IsoA2 { get; set; }

        [JsonPropertyName("iso_3166_2")]
        public string? Iso31662 { get; set; }

        [JsonPropertyName("adm1_code")]
        public string? Adm1Code { get; set; }

        [JsonPropertyName("ne_id")]
        public long? NeId { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("name_en")]
        public string? NameEn { get; set; }

        [JsonPropertyName("name_alt")]
        public string? NameAlt { get; set; }

        [JsonPropertyName("name_local")]
        public string? NameLocal { get; set; }

        [JsonPropertyName("gn_name")]
        public string? GnName { get; set; }

        [JsonPropertyName("woe_name")]
        public string? WoeName { get; set; }

        [JsonPropertyName("longitude")]
        public double? Longitude { get; set; }

        [JsonPropertyName("latitude")]
        public double? Latitude { get; set; }
    }

    [LoggerMessage(LogLevel.Information, "Downloading the Natural Earth admin-1 boundaries from {Url}; this happens once.")]
    static partial void LogDownloading(ILogger logger, string url);

    [LoggerMessage(LogLevel.Information, "Downloaded the Natural Earth admin-1 boundaries ({Bytes} bytes).")]
    static partial void LogDownloaded(ILogger logger, long bytes);

    [LoggerMessage(LogLevel.Information,
        "Read the regions of {Countries} countries ({Regions} regions, {Points} boundary points after simplification) in {Seconds:F1} s.")]
    static partial void LogLoaded(ILogger logger, int countries, int regions, int points, double seconds);

    [LoggerMessage(LogLevel.Warning, "The region boundaries could not be read; answers go out without region maps until the next try.")]
    static partial void LogUnavailable(ILogger logger, Exception exception);
}
