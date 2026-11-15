using UseCases.OutputPorts.AI;

namespace UseCases.UseCases.AI.Tools;

/// <summary>
/// The known towns and regions by name, for <see cref="ScreenshotPlaceReader"/>: every name and alias
/// normalised the way <see cref="RegionMention.Normalise"/> reads text, so "São Paulo" is found as
/// "sao paulo". Built once per list the directory returns; looked up per question.
/// </summary>
public sealed class PlaceIndex
{
    /// <summary>Longest name looked for, in words: "Saint-Jean-sur-Richelieu" is four.</summary>
    public const int MaxNameWords = 4;

    private const double EarthRadiusKm = 6371;

    private readonly Dictionary<string, List<KnownPlace>> _byName;
    private readonly Dictionary<string, List<KnownPlace>> _regionsByCode;
    private readonly IReadOnlyList<KnownPlace> _towns;

    private PlaceIndex(
        Dictionary<string, List<KnownPlace>> byName,
        Dictionary<string, List<KnownPlace>> regionsByCode,
        IReadOnlyList<KnownPlace> towns)
    {
        _byName = byName;
        _regionsByCode = regionsByCode;
        _towns = towns;
    }

    /// <summary>An index that knows no place, for when the datasets are not read yet.</summary>
    public static PlaceIndex Empty { get; } = new([], [], []);

    public bool IsEmpty => _byName.Count == 0;

    public static PlaceIndex Build(IEnumerable<KnownPlace> places)
    {
        ArgumentNullException.ThrowIfNull(places);

        var byName = new Dictionary<string, List<KnownPlace>>(StringComparer.Ordinal);
        var byCode = new Dictionary<string, List<KnownPlace>>(StringComparer.Ordinal);
        var towns = new List<KnownPlace>();

        foreach (var place in places)
        {
            foreach (var name in new[] { place.Name }.Concat(place.Aliases)
                         .Select(RegionMention.Normalise)
                         .Where(name => name.Length > 0 && name.Count(character => character == ' ') < MaxNameWords)
                         .Distinct(StringComparer.Ordinal))
            {
                Add(byName, name, place);
            }

            if (place.Kind == KnownPlaceKind.Region)
            {
                foreach (var code in place.Codes.Select(RegionMention.Normalise).Where(code => code.Length > 0).Distinct(StringComparer.Ordinal))
                {
                    Add(byCode, code, place);
                }
            }
            else
            {
                towns.Add(place);
            }
        }

        return new PlaceIndex(byName, byCode, towns);
    }

    /// <summary>The towns and regions going by <paramref name="normalisedName"/>; empty when none does.</summary>
    public IReadOnlyList<KnownPlace> Find(string normalisedName) =>
        _byName.TryGetValue(normalisedName, out var places) ? places : [];

    /// <summary>The regions written as <paramref name="normalisedCode"/> after a town ("ma" for Massachusetts — and Maranhão).</summary>
    public IReadOnlyList<KnownPlace> FindRegionsByCode(string normalisedCode) =>
        _regionsByCode.TryGetValue(normalisedCode, out var places) ? places : [];

    /// <summary>The town nearest the point within <paramref name="maxKm"/>, or null when none is that close.</summary>
    public KnownPlace? NearestTown(double latitude, double longitude, double maxKm)
    {
        KnownPlace? nearest = null;
        var nearestKm = maxKm;

        foreach (var town in _towns)
        {
            // A degree of latitude is 111 km: towns further than that in latitude alone are skipped unmeasured.
            if (Math.Abs(town.Latitude - latitude) * 111 > nearestKm)
            {
                continue;
            }

            var km = DistanceKm(latitude, longitude, town.Latitude, town.Longitude);
            if (km <= nearestKm)
            {
                nearestKm = km;
                nearest = town;
            }
        }

        return nearest;
    }

    /// <summary>The great-circle distance between two points, in kilometres.</summary>
    public static double DistanceKm(double latitudeA, double longitudeA, double latitudeB, double longitudeB)
    {
        static double Radians(double degrees) => degrees * Math.PI / 180;

        var dLatitude = Radians(latitudeB - latitudeA);
        var dLongitude = Radians(longitudeB - longitudeA);
        var a = Math.Sin(dLatitude / 2) * Math.Sin(dLatitude / 2)
                + Math.Cos(Radians(latitudeA)) * Math.Cos(Radians(latitudeB)) * Math.Sin(dLongitude / 2) * Math.Sin(dLongitude / 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    private static void Add(Dictionary<string, List<KnownPlace>> index, string key, KnownPlace place)
    {
        if (!index.TryGetValue(key, out var list))
        {
            index[key] = list = [];
        }

        if (!list.Contains(place))
        {
            list.Add(place);
        }
    }
}
