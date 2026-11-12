using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Tools;

namespace Infrastructure.OutputAdapters.AI.Gbif;

/// <summary>
/// Draws a species' records as a map from GBIF's own map tiles, and stores the picture in the image
/// relay so an answer can upload it.
///
/// Two layers of the same tiles, both free and without a key: GBIF's base map
/// (<c>tile.gbif.org/4326/omt/{z}/{x}/{y}@1x.png</c>, drawn from OpenStreetMap data — hence the credit
/// in the picture's footer) and, over it, the density of the species' occurrence records
/// (<c>api.gbif.org/v2/map/occurrence/density/…</c>), binned into hexagons, purple where there are few
/// and yellow where there are many. An empty density tile answers 204 and is simply not drawn. The
/// density is laid on a little translucent, so the state and country names of the base map still read
/// through it: the map is for telling regions apart.
///
/// The frame is the country a range was counted in, or the countries that hold the species' records
/// (<see cref="SpeciesMapPlan"/>). Records of the neighbours inside the frame are shown too: a cholla
/// does not stop at the border, and where it goes on is part of where it is.
///
/// Best effort throughout. The answer waits for the map, so it is given
/// <see cref="AiSpeciesRangeMapConfiguration.TimeoutSeconds"/> and no more; whatever goes wrong, the
/// range is offered in words. A drawn map is kept as long as the range it shows
/// (<see cref="AiSpeciesRangeConfiguration.CacheHours"/>).
/// </summary>
public sealed partial class GbifSpeciesRangeMap(
    IHttpClientFactory httpClientFactory,
    IImageRelay imageRelay,
    IOptions<AiToolsConfiguration> configuration,
    TimeProvider timeProvider,
    ILogger<GbifSpeciesRangeMap> logger) : ISpeciesRangeMap
{
    /// <summary>Tiles fetched at once. GBIF asks nothing of anonymous readers, but a dozen at a time is manners.</summary>
    private const int MaxConcurrentTiles = 6;

    /// <summary>Hexagons across one tile: about 17 pixels each, large enough to see and small enough to place.</summary>
    private const int HexagonsPerTile = 30;

    private const string DensityStyle = "purpleYellow-noborder.poly";

    /// <summary>How solid the records are drawn, of 255: enough to stand out, not enough to hide a state's name.</summary>
    private const byte DensityOpacity = 205;

    private const int FooterHeight = 38;

    private const int JpegQuality = 88;

    private const int MaxCachedMaps = 256;

    /// <summary>A map cut down to its records that comes out narrower than this is drawn again at a finer zoom.</summary>
    private const int MinSharpWidth = 1000;

    /// <summary>A pixel of a density tile at least this opaque is part of a drawn hexagon.</summary>
    private const byte DrawnAlpha = 32;

    /// <summary>Share of the drawn records left outside a tightened map at each of its four sides.</summary>
    private const double OutlierShare = 0.01;

    private readonly ConcurrentDictionary<string, CachedMap> _cache = new(StringComparer.Ordinal);

    public async Task<string?> DrawAsync(SpeciesRange range, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(range);

        var settings = configuration.Value.SpeciesRange;
        if (!settings.Enabled || !settings.Map.Enabled || !imageRelay.IsEnabled || range.TotalRecords <= 0)
        {
            return null;
        }

        var key = $"{range.TaxonKey.ToString(CultureInfo.InvariantCulture)}|{range.CountryCode}";
        var now = timeProvider.GetUtcNow();
        if (_cache.TryGetValue(key, out var cached) && cached.ExpiresAt > now)
        {
            return cached.Url;
        }

        if (SpeciesMapPlan.FrameOf(range) is not { } frame)
        {
            return null;
        }

        var plan = SpeciesMapPlan.For(frame);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, settings.Map.TimeoutSeconds)), timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            var client = httpClientFactory.CreateClient(GbifSpeciesRangeReader.HttpClientName);
            using var gate = new SemaphoreSlim(MaxConcurrentTiles);

            // The records first: where they are decides what the map shows.
            var records = await FetchAsync(client, plan, tile => DensityTileUrl(settings, tile, range), gate, linked.Token)
                .ConfigureAwait(false);

            // A country is shown whole, so it can be seen where in it the plant is not, and so is the
            // world: cut down to most of its records, a map of eucalyptus lost the Americas. A range
            // that keeps to one part of the world is cut down to where its records are.
            if (range.CountryCode is null && frame != SpeciesMapPlan.World && RecordsExtent(plan, records) is { } extent)
            {
                var tightened = plan.TightenedTo(extent.Left, extent.Top, extent.Right, extent.Bottom);

                // Cut out of tiles fetched for a far wider frame, a small range is a small picture with
                // large hexagons: it is drawn again one or more zooms finer, for the frame it now has.
                if (tightened.Width < MinSharpWidth && SpeciesMapPlan.For(tightened.Frame) is { } finer && finer.Zoom > plan.Zoom)
                {
                    plan = finer;
                    records = await FetchAsync(client, plan, tile => DensityTileUrl(settings, tile, range), gate, linked.Token)
                        .ConfigureAwait(false);
                }
                else
                {
                    plan = tightened;
                }
            }

            var baseMap = await FetchAsync(client, plan, tile => BaseTileUrl(settings, tile), gate, linked.Token).ConfigureAwait(false);
            var picture = Compose(plan, baseMap, records, Title(range));

            var stored = await imageRelay.StoreAsync(picture, "image/jpeg", linked.Token).ConfigureAwait(false);
            if (stored.IsFailure)
            {
                LogNotStored(logger, range.CanonicalName, stored.Error.Message);
                return null;
            }

            if (settings.CacheHours > 0)
            {
                if (_cache.Count >= MaxCachedMaps)
                {
                    _cache.Clear();
                }

                _cache[key] = new CachedMap(stored.Value, now + TimeSpan.FromHours(settings.CacheHours));
            }

            LogDrawn(logger, range.CanonicalName, range.CountryCode ?? "its range", plan.Zoom, plan.Tiles.Count, plan.Width, plan.Height, picture.Length);
            return stored.Value;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogTimedOut(logger, range.CanonicalName, settings.Map.TimeoutSeconds);
            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException)
        {
            LogFailed(logger, range.CanonicalName, exception);
            return null;
        }
    }

    private static string BaseTileUrl(AiSpeciesRangeConfiguration settings, MapTile tile) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{settings.Map.TileBaseUrl.TrimEnd('/')}/4326/omt/{tile.Zoom}/{tile.X}/{tile.Y}@1x.png?style={Uri.EscapeDataString(settings.Map.BaseMapStyle)}");

    private static string DensityTileUrl(AiSpeciesRangeConfiguration settings, MapTile tile, SpeciesRange range) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{settings.BaseUrl.TrimEnd('/')}/v2/map/occurrence/density/{tile.Zoom}/{tile.X}/{tile.Y}@1x.png"
            + $"?srs=EPSG:4326&taxonKey={range.TaxonKey}&bin=hex&hexPerTile={HexagonsPerTile}&style={DensityStyle}");

    /// <summary>
    /// One layer's tiles, side by side, in the plan's order. A tile outside what the map will show is
    /// not asked for, and is empty like a tile without records.
    /// </summary>
    private static async Task<IReadOnlyList<byte[]>> FetchAsync(
        HttpClient client,
        SpeciesMapPlan plan,
        Func<MapTile, string> url,
        SemaphoreSlim gate,
        CancellationToken cancellationToken) =>
        await Task.WhenAll(plan.Tiles.Select(tile => plan.Shows(tile)
            ? FetchTileAsync(client, url(tile), gate, cancellationToken)
            : Task.FromResult<byte[]>([]))).ConfigureAwait(false);

    /// <summary>One tile's bytes; empty when the tile holds nothing, which is how GBIF answers for a place without records.</summary>
    private static async Task<byte[]> FetchTileAsync(HttpClient client, string url, SemaphoreSlim gate, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return [];
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// What the map says it is. For a range counted within one country the map is framed on that
    /// country, but GBIF's density tiles take one filter, and it is the species: the records of the
    /// neighbours within the frame are drawn too. Cholla does not stop at the Mexican border, and a
    /// player is better off seeing that, so the title says "in and around" rather than claim less
    /// than is shown.
    /// </summary>
    internal static string Title(SpeciesRange range)
    {
        var name = string.IsNullOrWhiteSpace(range.CanonicalName) ? range.ScientificName : range.CanonicalName;
        var where = range.CountryCode is { } code ? $" in and around {IsoCountries.NameOf(code)}" : string.Empty;
        return $"{name}: GBIF occurrence records{where} (purple: few, yellow: many)";
    }

    /// <summary>
    /// Lays the tiles out, the records over the base map, cuts the frame out of them, and writes what
    /// the map shows and whose it is underneath.
    /// </summary>
    private static byte[] Compose(SpeciesMapPlan plan, IReadOnlyList<byte[]> baseMap, IReadOnlyList<byte[]> records, string title)
    {
        var info = new SKImageInfo(plan.Width, plan.Height + FooterHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info) ?? throw new InvalidDataException("No drawing surface could be created.");
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        canvas.Save();
        canvas.ClipRect(new SKRect(0, 0, plan.Width, plan.Height));
        canvas.Translate(-plan.CropX, -plan.CropY);

        using (var translucent = new SKPaint())
        {
            translucent.Color = SKColors.White.WithAlpha(DensityOpacity);

            // Every base tile before any record, or a tile's edge would cover its neighbour's hexagons.
            for (var index = 0; index < plan.Tiles.Count; index++)
            {
                // A base tile that will not decode would leave a hole where a country should be.
                if (plan.Shows(plan.Tiles[index]) && !Draw(canvas, baseMap[index], plan.Tiles[index], paint: null))
                {
                    throw new InvalidDataException("A base map tile could not be read.");
                }
            }

            for (var index = 0; index < plan.Tiles.Count; index++)
            {
                Draw(canvas, records[index], plan.Tiles[index], translucent);
            }
        }

        canvas.Restore();

        using var typeface = SKTypeface.FromFamilyName("Arial") ?? SKTypeface.Default;
        using var titleFont = new SKFont(typeface, 15);
        using var creditFont = new SKFont(typeface, 11);
        using var ink = new SKPaint();
        ink.Color = new SKColor(40, 40, 40);
        ink.IsAntialias = true;

        canvas.DrawText(title, 8, plan.Height + 16, SKTextAlign.Left, titleFont, ink);
        canvas.DrawText("Map data © OpenStreetMap contributors, © OpenMapTiles, GBIF", 8, plan.Height + 32, SKTextAlign.Left, creditFont, ink);

        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality)
                            ?? throw new InvalidDataException("The map could not be encoded.");
        return encoded.ToArray();
    }

    /// <returns>False when the tile held bytes that are no picture; an empty tile is drawn as nothing, and is fine.</returns>
    private static bool Draw(SKCanvas canvas, byte[] bytes, MapTile tile, SKPaint? paint)
    {
        if (bytes.Length == 0)
        {
            return true;
        }

        using var image = SKImage.FromEncodedData(bytes);
        if (image is null)
        {
            return false;
        }

        // Drawn pixel for pixel, so no sampling is wanted: a tile is placed, never scaled.
        canvas.DrawImage(image, tile.CanvasX, tile.CanvasY, SKSamplingOptions.Default, paint);
        return true;
    }

    /// <summary>
    /// Where the records are on the canvas, in pixels: the rectangle around the drawn hexagons, without
    /// the outermost <see cref="OutlierShare"/> of them on each side — one garden in Japan must not
    /// stretch a map of south-east Asia to Tokyo. Null when no tile holds any.
    /// </summary>
    private static (int Left, int Top, int Right, int Bottom)? RecordsExtent(SpeciesMapPlan plan, IReadOnlyList<byte[]> records)
    {
        var columns = new long[plan.Tiles.Max(tile => tile.CanvasX) + SpeciesMapPlan.TileSize];
        var rows = new long[plan.Tiles.Max(tile => tile.CanvasY) + SpeciesMapPlan.TileSize];
        long total = 0;

        for (var index = 0; index < plan.Tiles.Count; index++)
        {
            if (records[index].Length == 0)
            {
                continue;
            }

            using var bitmap = SKBitmap.Decode(records[index]);
            if (bitmap is null)
            {
                continue;
            }

            var pixels = bitmap.Pixels;
            var width = Math.Min(bitmap.Width, SpeciesMapPlan.TileSize);
            var height = Math.Min(bitmap.Height, SpeciesMapPlan.TileSize);

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (pixels[y * bitmap.Width + x].Alpha > DrawnAlpha)
                    {
                        columns[plan.Tiles[index].CanvasX + x]++;
                        rows[plan.Tiles[index].CanvasY + y]++;
                        total++;
                    }
                }
            }
        }

        if (total == 0)
        {
            return null;
        }

        var (left, right) = Trimmed(columns, total);
        var (top, bottom) = Trimmed(rows, total);
        return (left, top, right, bottom);
    }

    /// <summary>The first and last index between which all but <see cref="OutlierShare"/> at each end of the counts lie.</summary>
    private static (int First, int Last) Trimmed(long[] counts, long total)
    {
        var allowance = (long)(total * OutlierShare);

        var first = 0;
        for (long seen = 0; first < counts.Length - 1 && seen + counts[first] <= allowance; first++)
        {
            seen += counts[first];
        }

        var last = counts.Length - 1;
        for (long seen = 0; last > first && seen + counts[last] <= allowance; last--)
        {
            seen += counts[last];
        }

        return (first, last + 1);
    }

    private sealed record CachedMap(string Url, DateTimeOffset ExpiresAt);

    [LoggerMessage(LogLevel.Information,
        "Drew the GBIF map of {Name} ({Scope}): zoom {Zoom}, {Tiles} tile(s), {Width}x{Height} px, {Bytes} bytes.")]
    static partial void LogDrawn(ILogger logger, string name, string scope, int zoom, int tiles, int width, int height, int bytes);

    [LoggerMessage(LogLevel.Warning, "The GBIF map of {Name} took over {Seconds} s; its range is offered without a map.")]
    static partial void LogTimedOut(ILogger logger, string name, int seconds);

    [LoggerMessage(LogLevel.Warning, "The GBIF map of {Name} could not be drawn; its range is offered without a map.")]
    static partial void LogFailed(ILogger logger, string name, Exception exception);

    [LoggerMessage(LogLevel.Warning, "The GBIF map of {Name} could not be stored ({Reason}); its range is offered without a map.")]
    static partial void LogNotStored(ILogger logger, string name, string reason);
}
