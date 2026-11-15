using System.Globalization;
using UseCases.OutputPorts.AI;

namespace UseCases.UseCases.AI.Tools;

/// <summary>How exactly a member said where a screenshot was taken.</summary>
public enum PlacePrecision
{
    /// <summary>A town, or coordinates: "just south of Boston".</summary>
    Point,

    /// <summary>A state, province or county: "in Massachusetts".</summary>
    Region,

    /// <summary>A country: "this is Bulgaria".</summary>
    Country
}

/// <summary>
/// Where a member said a screenshot was taken, as far as they said it.
/// </summary>
/// <param name="Name">The place as shown: "Boston, Massachusetts, United States".</param>
/// <param name="CountryCode">ISO 3166 alpha-2; null only for coordinates far from any known town.</param>
/// <param name="Precision">A point, a region or a whole country.</param>
/// <param name="Latitude">A point's latitude, or a region's label point.</param>
/// <param name="Longitude">As <paramref name="Latitude"/>.</param>
/// <param name="Bounds">A region's rectangle.</param>
/// <param name="Said">What the member wrote: "south of Boston".</param>
public sealed record ScreenshotPlace(
    string Name,
    string? CountryCode,
    PlacePrecision Precision,
    double? Latitude,
    double? Longitude,
    PlaceBounds? Bounds,
    string Said)
{
    /// <summary>
    /// Where records count as "near": <paramref name="radiusKm"/> around a point, a region's rectangle,
    /// or the country. Null when none can be said.
    /// </summary>
    public RecordArea? LocalArea(double radiusKm) => Precision switch
    {
        PlacePrecision.Point when Latitude is { } latitude && Longitude is { } longitude => new CircleRecordArea(latitude, longitude, radiusKm),
        PlacePrecision.Region when Bounds is { CrossesAntimeridian: false } bounds => new BoxRecordArea(bounds.West, bounds.South, bounds.East, bounds.North),
        _ => CountryArea
    };

    /// <summary>The whole country, where a plant counts as recorded at all; null when the country is unknown.</summary>
    public RecordArea? CountryArea => CountryCode is { } code ? new CountryRecordArea(code) : null;

    /// <summary>Whether <see cref="LocalArea"/> is the country itself, so one count serves both.</summary>
    public bool IsCountryWide(double radiusKm) => LocalArea(radiusKm) is CountryRecordArea;

    /// <summary>"within 50 km of Boston", "in Massachusetts", "in Bulgaria": where the local records were counted.</summary>
    public string DescribeArea(double radiusKm) => LocalArea(radiusKm) switch
    {
        CircleRecordArea => $"within {radiusKm.ToString("0", CultureInfo.InvariantCulture)} km of {ShortName}",
        _ when Precision == PlacePrecision.Region => $"in {ShortName}",
        _ when CountryCode is { } code => $"in {IsoCountries.NameOf(code)}",
        _ => $"near {ShortName}"
    };

    /// <summary>The place's own name, without the region and country it lies in: "Boston", or the coordinates themselves.</summary>
    public string ShortName
    {
        get => _shortName ?? Name.Split(',')[0].Trim();
        init => _shortName = value;
    }

    private readonly string? _shortName;
}
