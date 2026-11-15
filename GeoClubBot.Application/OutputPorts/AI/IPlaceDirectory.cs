namespace UseCases.OutputPorts.AI;

/// <summary>
/// The towns and first-level regions a member may name as where a screenshot was taken — "just south of
/// Boston", "in Massachusetts" — with where each lies. From public datasets, downloaded once.
/// </summary>
public interface IPlaceDirectory
{
    /// <summary>
    /// Every place known, the same list (by reference) until it is read again; empty while the datasets
    /// are not read yet, or cannot be. Never throws: a place is a nicety, never worth failing an answer for.
    /// </summary>
    Task<IReadOnlyList<KnownPlace>> ReadAsync(CancellationToken cancellationToken = default);
}

public enum KnownPlaceKind
{
    /// <summary>A state, province, county or the like: a country's first-level division.</summary>
    Region,

    /// <summary>A town or city.</summary>
    Town
}

/// <param name="Kind">A region or a town.</param>
/// <param name="Name">The name to show: "Boston", "Massachusetts".</param>
/// <param name="Aliases">Other names it is written as: its ASCII spelling, a local or alternative name.</param>
/// <param name="CountryCode">ISO 3166 alpha-2.</param>
/// <param name="RegionKey">
/// The first-level region the place is, or lies in, as GeoNames keys it ("US.MA"): what ties a town to
/// its state. Null when the datasets do not say.
/// </param>
/// <param name="RegionName">For a town, its region's name when known ("Massachusetts"); for a region, null.</param>
/// <param name="Codes">
/// Short codes a region is written as after a town — "MA" in "Boston, MA". Never matched on their own:
/// two letters are too often a word.
/// </param>
/// <param name="Latitude">A town's centre; a region's label point, which lies inside it.</param>
/// <param name="Longitude">As <paramref name="Latitude"/>.</param>
/// <param name="Population">A town's; 0 for a region.</param>
/// <param name="Bounds">A region's rectangle; null for a town.</param>
public sealed record KnownPlace(
    KnownPlaceKind Kind,
    string Name,
    IReadOnlyList<string> Aliases,
    string CountryCode,
    string? RegionKey,
    string? RegionName,
    IReadOnlyList<string> Codes,
    double Latitude,
    double Longitude,
    long Population,
    PlaceBounds? Bounds);

/// <summary>A rectangle of latitude and longitude. West lies east of East only for a place across the antimeridian.</summary>
public sealed record PlaceBounds(double West, double South, double East, double North)
{
    /// <summary>Whether the rectangle wraps round the antimeridian, which no box filter can say.</summary>
    public bool CrossesAntimeridian => West > East;

    /// <summary>Its size in square degrees, roughly: what tells the larger of two same-named regions.</summary>
    public double Area => CrossesAntimeridian ? 0 : (East - West) * (North - South);
}
