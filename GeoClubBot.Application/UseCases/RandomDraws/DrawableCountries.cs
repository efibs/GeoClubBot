using Configuration;
using Extensions;
using UseCases.UseCases.AI.Tools;

namespace UseCases.UseCases.RandomDraws;

/// <summary>A country <c>/random country</c> can draw.</summary>
/// <param name="PlonkItUrl">Its PlonkIt guide; null for a country PlonkIt has no page for.</param>
public sealed record DrawableCountry(string Code, string Name, string Flag, Uri? PlonkItUrl);

/// <summary>
/// The countries <c>/random country</c> draws from: every country of <see cref="IsoCountries"/>, or only
/// those with official Street View coverage — the built-in list, or <c>RandomDraws:StreetViewCountryCodes</c>.
/// </summary>
public static class DrawableCountries
{
    /// <summary>
    /// Every country with a PlonkIt guide, by its page's path. PlonkIt writes a guide for each country
    /// with official coverage, so the list doubles as GeoGuessr's World map. Read from
    /// https://www.plonkit.net/sitemap.xml on 2026-11-22; Alaska, Hawaii, the Azores and Madeira have pages
    /// of their own but are drawn as part of their country.
    /// </summary>
    // Stryker disable once String : lookup data; a test checks every code and slug instead.
    private static readonly (string Code, string Slug)[] PlonkItPages =
    [
        ("AD", "andorra"), ("AE", "united-arab-emirates"), ("AL", "albania"), ("AQ", "antarctica"),
        ("AR", "argentina"), ("AS", "american-samoa"), ("AT", "austria"), ("AU", "australia"),
        ("BD", "bangladesh"), ("BE", "belgium"), ("BG", "bulgaria"), ("BM", "bermuda"), ("BO", "bolivia"),
        ("BR", "brazil"), ("BT", "bhutan"), ("BW", "botswana"), ("BY", "belarus"), ("CA", "canada"),
        ("CC", "cocos-islands"), ("CH", "switzerland"), ("CL", "chile"), ("CN", "china"), ("CO", "colombia"),
        ("CR", "costa-rica"), ("CW", "curacao"), ("CX", "christmas-island"), ("CY", "cyprus"),
        ("CZ", "czechia"), ("DE", "germany"), ("DK", "denmark"), ("DO", "dominican-republic"),
        ("EC", "ecuador"), ("EE", "estonia"), ("EG", "egypt"), ("ES", "spain"), ("FI", "finland"),
        ("FK", "falkland-islands"), ("FO", "faroe-islands"), ("FR", "france"), ("GB", "united-kingdom"),
        ("GH", "ghana"), ("GI", "gibraltar"), ("GL", "greenland"), ("GS", "south-georgia-sandwich-islands"),
        ("GR", "greece"), ("GT", "guatemala"), ("GU", "guam"), ("HK", "hong-kong"), ("HR", "croatia"),
        ("HU", "hungary"), ("ID", "indonesia"), ("IE", "ireland"), ("IL", "israel-west-bank"),
        ("IM", "isle-of-man"), ("IN", "india"), ("IO", "british-indian-ocean-territory"), ("IQ", "iraq"),
        ("IS", "iceland"), ("IT", "italy"), ("JE", "jersey"), ("JO", "jordan"), ("JP", "japan"),
        ("KE", "kenya"), ("KG", "kyrgyzstan"), ("KH", "cambodia"), ("KR", "south-korea"),
        ("KZ", "kazakhstan"), ("LA", "laos"), ("LB", "lebanon"), ("LI", "liechtenstein"),
        ("LK", "sri-lanka"), ("LS", "lesotho"), ("LT", "lithuania"), ("LU", "luxembourg"), ("LV", "latvia"),
        ("MC", "monaco"), ("ME", "montenegro"), ("MG", "madagascar"), ("MK", "north-macedonia"),
        ("ML", "mali"), ("MN", "mongolia"), ("MO", "macau"), ("MP", "northern-mariana-islands"),
        ("MQ", "martinique"), ("MT", "malta"), ("MX", "mexico"), ("MY", "malaysia"), ("NA", "namibia"),
        ("NG", "nigeria"), ("NL", "netherlands"), ("NO", "norway"), ("NP", "nepal"),
        ("NZ", "new-zealand"), ("OM", "oman"), ("PA", "panama"), ("PE", "peru"), ("PH", "philippines"),
        ("PK", "pakistan"), ("PL", "poland"), ("PM", "saint-pierre-and-miquelon"),
        ("PN", "pitcairn-islands"), ("PR", "puerto-rico"), ("PT", "portugal"), ("QA", "qatar"),
        ("RE", "reunion"), ("RO", "romania"), ("RS", "serbia"), ("RU", "russia"), ("RW", "rwanda"),
        ("SE", "sweden"), ("SG", "singapore"), ("SI", "slovenia"), ("SJ", "svalbard"), ("SK", "slovakia"),
        ("SM", "san-marino"), ("ST", "sao-tome-and-principe"), ("SN", "senegal"), ("SZ", "eswatini"),
        ("TH", "thailand"), ("TN", "tunisia"), ("TR", "turkey"), ("TW", "taiwan"), ("TZ", "tanzania"),
        ("UA", "ukraine"), ("UG", "uganda"), ("UM", "us-minor-outlying-islands"), ("US", "united-states"),
        ("UY", "uruguay"), ("VI", "us-virgin-islands"), ("VU", "vanuatu"), ("VN", "vietnam"),
        ("ZA", "south-africa")
    ];

    private static readonly Dictionary<string, string> SlugByCode =
        PlonkItPages.ToDictionary(page => page.Code, page => page.Slug, StringComparer.Ordinal);

    /// <summary>The codes of the built-in Street View list.</summary>
    public static IReadOnlyCollection<string> BuiltInStreetViewCodes => SlugByCode.Keys;

    /// <summary>
    /// The countries to draw from, by name: every country of the table, or those with official Street
    /// View coverage (<c>StreetViewCountryCodes</c> when set, else the built-in list).
    /// </summary>
    public static IReadOnlyList<DrawableCountry> For(RandomDrawsConfiguration configuration, bool streetViewOnly) =>
        Build(!streetViewOnly
            ? IsoCountries.Codes
            : configuration.StreetViewCountryCodes is { Count: > 0 } configured
                ? configured.Select(code => code.Trim().ToUpperInvariant()).Where(IsoCountries.IsCountry).Distinct()
                : SlugByCode.Keys);

    private static List<DrawableCountry> Build(IEnumerable<string> codes) =>
        codes
            .Select(code => new DrawableCountry(
                code,
                IsoCountries.NameOf(code),
                code.ToFlagEmoji(),
                SlugByCode.TryGetValue(code, out var slug) ? new Uri($"https://www.plonkit.net/{slug}") : null))
            .OrderBy(country => country.Name, StringComparer.Ordinal)
            .ToList();
}
