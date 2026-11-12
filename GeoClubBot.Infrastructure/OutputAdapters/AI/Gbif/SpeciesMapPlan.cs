using UseCases.OutputPorts.AI;

namespace Infrastructure.OutputAdapters.AI.Gbif;

/// <summary>One map tile to fetch, and where its top-left corner goes on the canvas before cropping.</summary>
internal readonly record struct MapTile(int Zoom, int X, int Y, int CanvasX, int CanvasY);

/// <summary>
/// What a species map shows and how it is put together: the frame in degrees, the zoom level, the
/// tiles covering the frame, and the part of the canvas they make that is kept.
///
/// GBIF serves its maps in plain latitude and longitude (EPSG:4326): at zoom 0 two tiles of 512 pixels
/// side by side cover the world, and each further zoom halves a tile's span. A degree is as many pixels
/// east as north, so a frame in degrees is a rectangle of pixels without any projection arithmetic.
///
/// Pure, so the frames and the tile sums are pinned by tests.
/// </summary>
internal sealed record SpeciesMapPlan(MapBounds Frame, int Zoom, IReadOnlyList<MapTile> Tiles, int CropX, int CropY, int Width, int Height)
{
    public const int TileSize = 512;

    /// <summary>The whole world as it is worth showing: without the polar caps, where nothing a player meets grows.</summary>
    internal static readonly MapBounds World = new(-180, -58, 180, 80);

    /// <summary>The width a map is aimed at, in pixels: wide enough to read a state's name, small enough to upload quickly.</summary>
    private const int TargetWidth = 1400;

    /// <summary>Most tiles one map is made of, per layer. Past it the map is drawn one zoom coarser.</summary>
    private const int MaxTiles = 24;

    private const int MaxZoom = 8;

    /// <summary>A country holding this share of a plant's records is part of where it grows; less is a garden or a plantation.</summary>
    private const double MinShareOfRecords = 0.05;

    /// <summary>A frame wider than this is the world's.</summary>
    private const double MaxFrameWidth = 200;

    /// <summary>The smallest frame, in degrees: a small country is shown with its neighbours, or it is a map of nothing.</summary>
    private const double MinFrameWidth = 24;

    private const double MinFrameHeight = 14;

    /// <summary>A frame is at most this many times as wide as tall, and as tall as wide: Discord shows a strip of a map as a sliver.</summary>
    private const double MaxAspect = 2.2;

    private const double MaxInverseAspect = 1.25;

    /// <summary>Room left around the frame, as a share of its size.</summary>
    private const double Margin = 0.06;

    /// <summary>
    /// The part of the world to show for <paramref name="range"/>: the country it was counted in, or —
    /// counted by country — the countries that hold its records. Null when the range is of a country
    /// the table does not know, which is then not drawn rather than drawn wrong.
    /// </summary>
    public static MapBounds? FrameOf(SpeciesRange range)
    {
        ArgumentNullException.ThrowIfNull(range);

        if (range.CountryCode is { } code)
        {
            return CountryBounds.Of(code) is { } country ? Shape(country) : null;
        }

        var total = range.Areas.Sum(area => area.Records);
        MapBounds? held = null;

        foreach (var area in range.Areas)
        {
            if (total <= 0 || area.Records < total * MinShareOfRecords || CountryBounds.Of(area.Name) is not { } country)
            {
                continue;
            }

            held = held is { } so ? so.Union(country) : country;
        }

        return held is { } frame && frame.Width <= MaxFrameWidth ? Shape(frame) : World;
    }

    /// <summary>The tiles and the crop for a frame.</summary>
    public static SpeciesMapPlan For(MapBounds frame)
    {
        // The zoom at which the frame is nearest the target width, leaning to the finer one: a map a
        // little large still reads, and the whole world at zoom 0 is a thousand pixels of nothing much.
        var zoom = Math.Clamp(
            (int)Math.Ceiling(Math.Log2(TargetWidth * 180d / (TileSize * Math.Max(frame.Width, 1))) - 0.25), 0, MaxZoom);

        while (true)
        {
            var span = 180d / (1 << zoom);
            var columns = 2 << zoom;
            var rows = 1 << zoom;

            var firstX = Math.Clamp((int)Math.Floor((frame.West + 180) / span), 0, columns - 1);
            var lastX = Math.Clamp((int)Math.Ceiling((frame.East + 180) / span) - 1, firstX, columns - 1);
            var firstY = Math.Clamp((int)Math.Floor((90 - frame.North) / span), 0, rows - 1);
            var lastY = Math.Clamp((int)Math.Ceiling((90 - frame.South) / span) - 1, firstY, rows - 1);

            var count = (lastX - firstX + 1) * (lastY - firstY + 1);
            if (count > MaxTiles && zoom > 0)
            {
                zoom--;
                continue;
            }

            var tiles = new List<MapTile>(count);
            for (var y = firstY; y <= lastY; y++)
            {
                for (var x = firstX; x <= lastX; x++)
                {
                    tiles.Add(new MapTile(zoom, x, y, (x - firstX) * TileSize, (y - firstY) * TileSize));
                }
            }

            var pixelsPerDegree = TileSize / span;
            var canvasWest = firstX * span - 180;
            var canvasNorth = 90 - firstY * span;
            var canvasWidth = (lastX - firstX + 1) * TileSize;
            var canvasHeight = (lastY - firstY + 1) * TileSize;

            var cropX = Math.Clamp((int)Math.Round((frame.West - canvasWest) * pixelsPerDegree), 0, canvasWidth - 1);
            var cropY = Math.Clamp((int)Math.Round((canvasNorth - frame.North) * pixelsPerDegree), 0, canvasHeight - 1);
            var width = Math.Clamp((int)Math.Round(frame.Width * pixelsPerDegree), 1, canvasWidth - cropX);
            var height = Math.Clamp((int)Math.Round(frame.Height * pixelsPerDegree), 1, canvasHeight - cropY);

            return new SpeciesMapPlan(frame, zoom, tiles, cropX, cropY, width, height);
        }
    }

    /// <summary>Whether any of a tile ends up in the picture: one wholly outside the crop need not be fetched.</summary>
    public bool Shows(MapTile tile) =>
        tile.CanvasX < CropX + Width && tile.CanvasX + TileSize > CropX
        && tile.CanvasY < CropY + Height && tile.CanvasY + TileSize > CropY;

    /// <summary>Room left around the records a map is cut down to, as a share of their extent.</summary>
    private const double RecordsMargin = 0.15;

    /// <summary>The smallest part of the world a map is cut down to, in degrees.</summary>
    private const double MinRecordsWidth = 36;

    private const double MinRecordsHeight = 20;

    /// <summary>A map is cut down only when that leaves less than this share of it: a sliver saved is not worth a second frame.</summary>
    private const double WorthTightening = 0.85;

    /// <summary>
    /// The plan cut down to where the records are: the frame of a species counted across the world is
    /// the countries holding its records, and China alone is sixty degrees wide while the Khasi pine
    /// keeps to its south-western corner. The extent comes from the density tiles themselves — where
    /// they are not empty — so it costs no further request. Never cut below a size worth looking at, nor
    /// to a strip, and left as it was when little would be gained.
    /// </summary>
    /// <param name="left">The records' extent on the canvas the tiles make, in pixels: its left edge.</param>
    public SpeciesMapPlan TightenedTo(int left, int top, int right, int bottom)
    {
        var span = 180d / (1 << Zoom);
        var pixelsPerDegree = TileSize / span;
        var canvasWidth = Tiles.Max(tile => tile.CanvasX) + TileSize;
        var canvasHeight = Tiles.Max(tile => tile.CanvasY) + TileSize;

        var width = Math.Max((right - left) * (1 + 2 * RecordsMargin), MinRecordsWidth * pixelsPerDegree);
        var height = Math.Max((bottom - top) * (1 + 2 * RecordsMargin), MinRecordsHeight * pixelsPerDegree);

        width = Math.Max(width, height / MaxInverseAspect);
        height = Math.Max(height, width / MaxAspect);

        width = Math.Min(width, canvasWidth);
        height = Math.Min(height, canvasHeight);

        if (width * height >= (double)Width * Height * WorthTightening)
        {
            return this;
        }

        var x = Math.Clamp((left + right) / 2d - width / 2, 0, canvasWidth - width);
        var y = Math.Clamp((top + bottom) / 2d - height / 2, 0, canvasHeight - height);

        var canvasWest = Tiles.Min(tile => tile.X) * span - 180;
        var canvasNorth = 90 - Tiles.Min(tile => tile.Y) * span;

        return this with
        {
            CropX = (int)Math.Round(x),
            CropY = (int)Math.Round(y),
            Width = Math.Max(1, (int)Math.Floor(width)),
            Height = Math.Max(1, (int)Math.Floor(height)),
            Frame = new MapBounds(
                canvasWest + x / pixelsPerDegree,
                canvasNorth - (y + height) / pixelsPerDegree,
                canvasWest + (x + width) / pixelsPerDegree,
                canvasNorth - y / pixelsPerDegree)
        };
    }

    /// <summary>
    /// A frame fit to be looked at: with a margin, no smaller than a small country and its neighbours,
    /// neither a strip nor a column, and within the world.
    /// </summary>
    private static MapBounds Shape(MapBounds bounds)
    {
        var width = Math.Max(bounds.Width * (1 + 2 * Margin), MinFrameWidth);
        var height = Math.Max(bounds.Height * (1 + 2 * Margin), MinFrameHeight);

        width = Math.Max(width, height / MaxInverseAspect);
        height = Math.Max(height, width / MaxAspect);

        width = Math.Min(width, 360);
        height = Math.Min(height, World.Height);

        var centreX = (bounds.West + bounds.East) / 2;
        var centreY = (bounds.South + bounds.North) / 2;

        // Moved back inside the world rather than cut, so the frame keeps its shape at the edges.
        var west = Math.Clamp(centreX - width / 2, -180, 180 - width);
        var south = Math.Clamp(centreY - height / 2, World.South, World.North - height);

        return new MapBounds(west, south, west + width, south + height);
    }
}
