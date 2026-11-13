namespace UseCases.OutputPorts.AI;

/// <summary>
/// The first-level regions of each country — states, provinces, counties — by name, from a public
/// boundary dataset (Natural Earth). What an answer's "Lạng Sơn and Quảng Ninh" is matched against.
/// </summary>
public interface IRegionBoundaries
{
    /// <summary>
    /// The regions of a country, by ISO 3166 alpha-2 code; empty when the country is unknown, or the
    /// dataset is not loaded yet. Never throws: a map is a nicety, never worth failing an answer for.
    /// </summary>
    Task<IReadOnlyList<RegionName>> ReadNamesAsync(string countryCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// The country and first-level region a point lies in, or null when it lies in none the dataset
    /// draws (the sea, or the dataset not loaded yet). Never throws.
    /// </summary>
    Task<RegionPlace?> LocateAsync(double latitude, double longitude, CancellationToken cancellationToken = default);
}

/// <param name="CountryCode">ISO 3166 alpha-2.</param>
/// <param name="RegionName">The region's shown name.</param>
public sealed record RegionPlace(string CountryCode, string RegionName);

/// <param name="Id">The dataset's own id for the region, which a map request names it by.</param>
/// <param name="Name">The name to show: the English one where the dataset has it, else the local one.</param>
/// <param name="Aliases">Other spellings an answer may use: the local name, alternatives, the geonames one.</param>
public sealed record RegionName(string Id, string Name, IReadOnlyList<string> Aliases)
{
    /// <summary>Every form the region goes by, the shown name first, none twice.</summary>
    public IEnumerable<string> AllNames => new[] { Name }.Concat(Aliases).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Draws a country with some of its regions highlighted, and stores the picture where an answer can attach it.</summary>
public interface IRegionMapDrawer
{
    /// <summary>The picture's URL, or null when it could not be drawn: a country the frames do not know, the tiles unreachable, the dataset not loaded.</summary>
    Task<string?> DrawAsync(RegionMapRequest request, CancellationToken cancellationToken = default);
}

/// <param name="CountryCode">ISO 3166 alpha-2.</param>
/// <param name="Regions">What to highlight, each with how strongly.</param>
/// <param name="Title">Written under the map: what it shows.</param>
/// <param name="Shaded">True when the weights differ and the map should say what the shades mean.</param>
public sealed record RegionMapRequest(string CountryCode, IReadOnlyList<RegionHighlight> Regions, string Title, bool Shaded = false);

/// <param name="Id">The region's <see cref="RegionName.Id"/>.</param>
/// <param name="Label">What is written on the region.</param>
/// <param name="Weight">How strongly it is coloured, 0 to 1: how common the thing is there, or 1 for a plain highlight.</param>
public sealed record RegionHighlight(string Id, string Label, double Weight);
