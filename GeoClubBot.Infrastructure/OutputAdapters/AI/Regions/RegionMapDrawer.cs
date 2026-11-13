using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using Configuration;
using Infrastructure.OutputAdapters.AI.Gbif;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;
using UseCases.OutputPorts.AI;

namespace Infrastructure.OutputAdapters.AI.Regions;

/// <summary>
/// Draws a country with some of its regions coloured in: GBIF's base map tiles (OpenStreetMap data,
/// free, no key — the same tiles the species maps use), framed on the country as a species map is,
/// and over them the regions' outlines from Natural Earth, filled red by their weight and labelled.
/// Stored in the image relay so an answer can upload it. Best effort throughout: whatever fails, the
/// answer goes out without the map.
/// </summary>
public sealed partial class RegionMapDrawer(
    IHttpClientFactory httpClientFactory,
    NaturalEarthRegionBoundaries boundaries,
    IImageRelay imageRelay,
    IOptions<AiToolsConfiguration> configuration,
    TimeProvider timeProvider,
    ILogger<RegionMapDrawer> logger) : IRegionMapDrawer
{
    private const int MaxConcurrentTiles = 6;

    private const int FooterHeight = 38;

    private const int JpegQuality = 88;

    private const int MaxCachedMaps = 256;

    /// <summary>How solid a region at full weight is, of 255; a region at weight zero is still faintly there.</summary>
    private const byte FullOpacity = 170;

    private const byte MinOpacity = 40;

    private static readonly SKColor Highlight = new(200, 30, 30);

    private static readonly SKColor Outline = new(120, 10, 10);

    private readonly ConcurrentDictionary<string, CachedMap> _cache = new(StringComparer.Ordinal);

    public async Task<string?> DrawAsync(RegionMapRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tools = configuration.Value;
        var settings = tools.RegionMap;
        if (!settings.Enabled || !imageRelay.IsEnabled || request.Regions.Count == 0)
        {
            return null;
        }

        var key = $"{request.CountryCode}|{string.Join(";", request.Regions.Select(region => $"{region.Id}:{region.Weight.ToString("F2", CultureInfo.InvariantCulture)}"))}|{request.Title}";
        var now = timeProvider.GetUtcNow();
        if (_cache.TryGetValue(key, out var cached) && cached.ExpiresAt > now)
        {
            return cached.Url;
        }

        if (SpeciesMapPlan.ForCountry(request.CountryCode) is not { } plan)
        {
            LogNoFrame(logger, request.CountryCode);
            return null;
        }

        try
        {
            var shapes = await boundaries.ReadShapesAsync(request.CountryCode, cancellationToken).ConfigureAwait(false);
            var drawn = request.Regions
                .Select(region => (Region: region, Shape: shapes.FirstOrDefault(shape => shape.Id == region.Id)))
                .Where(entry => entry.Shape is not null)
                .ToList();

            if (drawn.Count == 0)
            {
                return null;
            }

            var client = httpClientFactory.CreateClient(GbifSpeciesRangeReader.HttpClientName);
            using var gate = new SemaphoreSlim(MaxConcurrentTiles);
            var tiles = await Task.WhenAll(plan.Tiles.Select(tile => plan.Shows(tile)
                ? FetchTileAsync(client, BaseTileUrl(tools.SpeciesRange, tile), gate, cancellationToken)
                : Task.FromResult<byte[]>([]))).ConfigureAwait(false);

            var picture = Compose(plan, tiles, drawn!, request);

            var stored = await imageRelay.StoreAsync(picture, "image/jpeg", cancellationToken).ConfigureAwait(false);
            if (stored.IsFailure)
            {
                LogNotStored(logger, request.CountryCode, stored.Error.Message);
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

            LogDrawn(logger, drawn.Count, request.CountryCode, plan.Zoom, plan.Width, plan.Height, picture.Length);
            return stored.Value;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException
                                              || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogFailed(logger, request.CountryCode, exception);
            return null;
        }
    }

    private static string BaseTileUrl(AiSpeciesRangeConfiguration settings, MapTile tile) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{settings.Map.TileBaseUrl.TrimEnd('/')}/4326/omt/{tile.Zoom}/{tile.X}/{tile.Y}@1x.png?style={Uri.EscapeDataString(settings.Map.BaseMapStyle)}");

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

    /// <summary>The tiles, the regions over them, their names, and the title and credits underneath.</summary>
    internal static byte[] Compose(
        SpeciesMapPlan plan,
        IReadOnlyList<byte[]> tiles,
        IReadOnlyList<(RegionHighlight Region, RegionShape Shape)> regions,
        RegionMapRequest request)
    {
        var info = new SKImageInfo(plan.Width, plan.Height + FooterHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info) ?? throw new InvalidDataException("No drawing surface could be created.");
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        canvas.Save();
        canvas.ClipRect(new SKRect(0, 0, plan.Width, plan.Height));

        for (var index = 0; index < plan.Tiles.Count; index++)
        {
            if (!plan.Shows(plan.Tiles[index]) || tiles[index].Length == 0)
            {
                continue;
            }

            using var image = SKImage.FromEncodedData(tiles[index]) ?? throw new InvalidDataException("A base map tile could not be read.");
            canvas.DrawImage(image, plan.Tiles[index].CanvasX - plan.CropX, plan.Tiles[index].CanvasY - plan.CropY, SKSamplingOptions.Default);
        }

        using var fill = new SKPaint();
        fill.IsAntialias = true;
        fill.Style = SKPaintStyle.Fill;

        using var stroke = new SKPaint();
        stroke.IsAntialias = true;
        stroke.Style = SKPaintStyle.Stroke;
        stroke.StrokeWidth = 1.5f;
        stroke.Color = Outline;

        foreach (var (region, shape) in regions)
        {
            var builder = new SKPathBuilder { FillType = SKPathFillType.EvenOdd };
            foreach (var ring in shape.Rings)
            {
                var first = true;
                foreach (var (longitude, latitude) in ring)
                {
                    var (x, y) = plan.ToPicture(longitude, latitude);
                    if (first)
                    {
                        builder.MoveTo(x, y);
                        first = false;
                    }
                    else
                    {
                        builder.LineTo(x, y);
                    }
                }

                builder.Close();
            }

            using var path = builder.Detach();

            var opacity = (byte)Math.Round(MinOpacity + (FullOpacity - MinOpacity) * Math.Clamp(region.Weight, 0, 1));
            fill.Color = Highlight.WithAlpha(opacity);
            canvas.DrawPath(path, fill);
            canvas.DrawPath(path, stroke);
        }

        using var typeface = SKTypeface.FromFamilyName("Arial") ?? SKTypeface.Default;
        using var labelFont = new SKFont(typeface, 14);
        using var titleFont = new SKFont(typeface, 15);
        using var creditFont = new SKFont(typeface, 11);

        using var labelInk = new SKPaint();
        labelInk.Color = new SKColor(20, 20, 20);
        labelInk.IsAntialias = true;

        using var halo = new SKPaint();
        halo.Color = SKColors.White.WithAlpha(230);
        halo.IsAntialias = true;
        halo.Style = SKPaintStyle.Stroke;
        halo.StrokeWidth = 3f;

        foreach (var (region, shape) in regions)
        {
            var (x, y) = plan.ToPicture(shape.LabelLongitude, shape.LabelLatitude);
            var label = request.Shaded
                ? $"{region.Label} ({Math.Round(Math.Clamp(region.Weight, 0, 1) * 100).ToString(CultureInfo.InvariantCulture)} %)"
                : region.Label;

            canvas.DrawText(label, x, y, SKTextAlign.Center, labelFont, halo);
            canvas.DrawText(label, x, y, SKTextAlign.Center, labelFont, labelInk);
        }

        canvas.Restore();

        using var ink = new SKPaint();
        ink.Color = new SKColor(40, 40, 40);
        ink.IsAntialias = true;

        canvas.DrawText(request.Title, 8, plan.Height + 16, SKTextAlign.Left, titleFont, ink);
        canvas.DrawText("Map data © OpenStreetMap contributors, © OpenMapTiles, GBIF; boundaries: Natural Earth", 8, plan.Height + 32, SKTextAlign.Left, creditFont, ink);

        using var snapshot = surface.Snapshot();
        using var encoded = snapshot.Encode(SKEncodedImageFormat.Jpeg, JpegQuality)
                            ?? throw new InvalidDataException("The map could not be encoded.");
        return encoded.ToArray();
    }

    private sealed record CachedMap(string Url, DateTimeOffset ExpiresAt);

    [LoggerMessage(LogLevel.Information, "Drew a region map of {Count} region(s) of {Country}: zoom {Zoom}, {Width}x{Height} px, {Bytes} bytes.")]
    static partial void LogDrawn(ILogger logger, int count, string country, int zoom, int width, int height, int bytes);

    [LoggerMessage(LogLevel.Debug, "No map frame is known for {Country}; no region map drawn.")]
    static partial void LogNoFrame(ILogger logger, string country);

    [LoggerMessage(LogLevel.Warning, "The region map of {Country} could not be drawn.")]
    static partial void LogFailed(ILogger logger, string country, Exception exception);

    [LoggerMessage(LogLevel.Warning, "The region map of {Country} could not be stored ({Reason}).")]
    static partial void LogNotStored(ILogger logger, string country, string reason);
}
