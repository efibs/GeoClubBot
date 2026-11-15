using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Tools;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// A few real towns and regions, as the GeoNames and Natural Earth datasets give them, chosen for the
/// ways a member's words can mislead: two Bostons and two Quincys, Montana a US state and a Bulgarian
/// province and town, Georgia a country and a state, Victoria a state and a city, Spring a Texan town.
/// </summary>
internal static class PlaceFixtures
{
    public static readonly KnownPlace Massachusetts = Region("Massachusetts", "US", "US.MA", "MA", 42.37, -72.00, new PlaceBounds(-73.51, 41.24, -69.93, 42.89), "Commonwealth of Massachusetts", "Mass.");
    public static readonly KnownPlace Illinois = Region("Illinois", "US", "US.IL", "IL", 40.06, -89.20, new PlaceBounds(-91.51, 36.97, -87.02, 42.51));
    public static readonly KnownPlace MontanaState = Region("Montana", "US", "US.MT", "MT", 47.00, -110.04, new PlaceBounds(-116.05, 44.36, -104.04, 49.00));
    public static readonly KnownPlace MontanaProvince = Region("Montana", "BG", "BG.47", "MT", 43.49, 23.18, new PlaceBounds(22.67, 43.18, 23.66, 43.83), "Mikhaylovgrad");
    public static readonly KnownPlace GeorgiaState = Region("Georgia", "US", "US.GA", "GA", 32.68, -83.22, new PlaceBounds(-85.61, 30.36, -80.84, 35.00));
    public static readonly KnownPlace Vermont = Region("Vermont", "US", "US.VT", "VT", 44.07, -72.67, new PlaceBounds(-73.44, 42.73, -71.46, 45.02));
    public static readonly KnownPlace VictoriaState = Region("Victoria", "AU", "AU.07", "VIC", -36.85, 144.28, new PlaceBounds(140.96, -39.16, 149.98, -33.98));
    public static readonly KnownPlace Maranhao = Region("Maranhão", "BR", "BR.13", "MA", -5.07, -45.27, new PlaceBounds(-48.76, -10.26, -41.80, -1.04));

    public static readonly KnownPlace Boston = Town("Boston", "US", "US.MA", "Massachusetts", 42.35843, -71.05977, 653_833);
    public static readonly KnownPlace BostonEngland = Town("Boston", "GB", null, null, 52.97633, -0.02664, 45_339);
    public static readonly KnownPlace Quincy = Town("Quincy", "US", "US.MA", "Massachusetts", 42.25288, -71.00227, 93_618);
    public static readonly KnownPlace QuincyIllinois = Town("Quincy", "US", "US.IL", "Illinois", 39.9356, -91.40987, 40_780);
    public static readonly KnownPlace Atlanta = Town("Atlanta", "US", "US.GA", "Georgia", 33.749, -84.38798, 463_878);
    public static readonly KnownPlace MontanaTown = Town("Montana", "BG", "BG.47", "Montana", 43.4125, 23.225, 47_445);
    public static readonly KnownPlace VictoriaCity = Town("Victoria", "CA", null, null, 48.43294, -123.3693, 289_625);
    public static readonly KnownPlace Spring = Town("Spring", "US", null, null, 30.07994, -95.41716, 62_559);
    public static readonly KnownPlace Reading = Town("Reading", "GB", null, null, 51.45625, -0.97113, 244_070);
    public static readonly KnownPlace Saigon = Town("Ho Chi Minh City", "VN", null, null, 10.82302, 106.62965, 3_467_331, "Saigon", "Sai Gon");
    public static readonly KnownPlace Aleppo = Town("Aleppo", "SY", null, null, 36.20124, 37.16117, 1_602_264);

    public static IReadOnlyList<KnownPlace> All { get; } =
    [
        Massachusetts, Illinois, MontanaState, MontanaProvince, GeorgiaState, Vermont, VictoriaState, Maranhao,
        Boston, BostonEngland, Quincy, QuincyIllinois, Atlanta, MontanaTown, VictoriaCity, Spring, Reading, Saigon, Aleppo
    ];

    public static PlaceIndex Index { get; } = PlaceIndex.Build(All);

    private static KnownPlace Region(string name, string country, string key, string code, double latitude, double longitude, PlaceBounds bounds, params string[] aliases) =>
        new(KnownPlaceKind.Region, name, aliases, country, key, null, [code], latitude, longitude, 0, bounds);

    private static KnownPlace Town(string name, string country, string? key, string? region, double latitude, double longitude, long population, params string[] aliases) =>
        new(KnownPlaceKind.Town, name, aliases, country, key, region, [], latitude, longitude, population, null);
}
