using UseCases.UseCases.AI.Retrieval;

namespace UseCases.UseCases.AI.Tools;

/// <summary>
/// ISO 3166-1 alpha-2 codes and the English names players use for them: the species database counts
/// records by code ("PH 256, CN 105"), and the model names a country however it likes ("Philippines",
/// "the Philippenes", "Sabah").
///
/// A table rather than <see cref="System.Globalization.RegionInfo"/>, whose names depend on the ICU
/// data of whichever machine runs the bot ("Myanmar (Burma)", "Türkiye", or nothing at all under
/// invariant globalisation) — the same answer should read the same everywhere, and in tests.
/// </summary>
public static class IsoCountries
{
    private const string Table =
        "AD:Andorra|AE:United Arab Emirates|AF:Afghanistan|AG:Antigua and Barbuda|AI:Anguilla|AL:Albania|"
        + "AM:Armenia|AO:Angola|AQ:Antarctica|AR:Argentina|AS:American Samoa|AT:Austria|AU:Australia|AW:Aruba|"
        + "AX:Åland Islands|AZ:Azerbaijan|BA:Bosnia and Herzegovina|BB:Barbados|BD:Bangladesh|BE:Belgium|"
        + "BF:Burkina Faso|BG:Bulgaria|BH:Bahrain|BI:Burundi|BJ:Benin|BL:Saint Barthélemy|BM:Bermuda|BN:Brunei|"
        + "BO:Bolivia|BQ:Caribbean Netherlands|BR:Brazil|BS:Bahamas|BT:Bhutan|BV:Bouvet Island|BW:Botswana|"
        + "BY:Belarus|BZ:Belize|CA:Canada|CC:Cocos Islands|CD:DR Congo|CF:Central African Republic|"
        + "CG:Republic of the Congo|CH:Switzerland|CI:Côte d'Ivoire|CK:Cook Islands|CL:Chile|CM:Cameroon|"
        + "CN:China|CO:Colombia|CR:Costa Rica|CU:Cuba|CV:Cape Verde|CW:Curaçao|CX:Christmas Island|CY:Cyprus|"
        + "CZ:Czechia|DE:Germany|DJ:Djibouti|DK:Denmark|DM:Dominica|DO:Dominican Republic|DZ:Algeria|"
        + "EC:Ecuador|EE:Estonia|EG:Egypt|EH:Western Sahara|ER:Eritrea|ES:Spain|ET:Ethiopia|FI:Finland|FJ:Fiji|"
        + "FK:Falkland Islands|FM:Micronesia|FO:Faroe Islands|FR:France|GA:Gabon|GB:United Kingdom|GD:Grenada|"
        + "GE:Georgia|GF:French Guiana|GG:Guernsey|GH:Ghana|GI:Gibraltar|GL:Greenland|GM:Gambia|GN:Guinea|"
        + "GP:Guadeloupe|GQ:Equatorial Guinea|GR:Greece|GS:South Georgia and the South Sandwich Islands|"
        + "GT:Guatemala|GU:Guam|GW:Guinea-Bissau|GY:Guyana|HK:Hong Kong|HM:Heard Island and McDonald Islands|"
        + "HN:Honduras|HR:Croatia|HT:Haiti|HU:Hungary|ID:Indonesia|IE:Ireland|IL:Israel|IM:Isle of Man|IN:India|"
        + "IO:British Indian Ocean Territory|IQ:Iraq|IR:Iran|IS:Iceland|IT:Italy|JE:Jersey|JM:Jamaica|JO:Jordan|"
        + "JP:Japan|KE:Kenya|KG:Kyrgyzstan|KH:Cambodia|KI:Kiribati|KM:Comoros|KN:Saint Kitts and Nevis|"
        + "KP:North Korea|KR:South Korea|KW:Kuwait|KY:Cayman Islands|KZ:Kazakhstan|LA:Laos|LB:Lebanon|"
        + "LC:Saint Lucia|LI:Liechtenstein|LK:Sri Lanka|LR:Liberia|LS:Lesotho|LT:Lithuania|LU:Luxembourg|"
        + "LV:Latvia|LY:Libya|MA:Morocco|MC:Monaco|MD:Moldova|ME:Montenegro|MF:Saint Martin|MG:Madagascar|"
        + "MH:Marshall Islands|MK:North Macedonia|ML:Mali|MM:Myanmar|MN:Mongolia|MO:Macau|"
        + "MP:Northern Mariana Islands|MQ:Martinique|MR:Mauritania|MS:Montserrat|MT:Malta|MU:Mauritius|"
        + "MV:Maldives|MW:Malawi|MX:Mexico|MY:Malaysia|MZ:Mozambique|NA:Namibia|NC:New Caledonia|NE:Niger|"
        + "NF:Norfolk Island|NG:Nigeria|NI:Nicaragua|NL:Netherlands|NO:Norway|NP:Nepal|NR:Nauru|NU:Niue|"
        + "NZ:New Zealand|OM:Oman|PA:Panama|PE:Peru|PF:French Polynesia|PG:Papua New Guinea|PH:Philippines|"
        + "PK:Pakistan|PL:Poland|PM:Saint Pierre and Miquelon|PN:Pitcairn Islands|PR:Puerto Rico|PS:Palestine|"
        + "PT:Portugal|PW:Palau|PY:Paraguay|QA:Qatar|RE:Réunion|RO:Romania|RS:Serbia|RU:Russia|RW:Rwanda|"
        + "SA:Saudi Arabia|SB:Solomon Islands|SC:Seychelles|SD:Sudan|SE:Sweden|SG:Singapore|SH:Saint Helena|"
        + "SI:Slovenia|SJ:Svalbard and Jan Mayen|SK:Slovakia|SL:Sierra Leone|SM:San Marino|SN:Senegal|"
        + "SO:Somalia|SR:Suriname|SS:South Sudan|ST:São Tomé and Príncipe|SV:El Salvador|SX:Sint Maarten|"
        + "SY:Syria|SZ:Eswatini|TC:Turks and Caicos Islands|TD:Chad|TF:French Southern Territories|TG:Togo|"
        + "TH:Thailand|TJ:Tajikistan|TK:Tokelau|TL:Timor-Leste|TM:Turkmenistan|TN:Tunisia|TO:Tonga|TR:Turkey|"
        + "TT:Trinidad and Tobago|TV:Tuvalu|TW:Taiwan|TZ:Tanzania|UA:Ukraine|UG:Uganda|"
        + "UM:United States Minor Outlying Islands|US:United States|UY:Uruguay|UZ:Uzbekistan|VA:Vatican City|"
        + "VC:Saint Vincent and the Grenadines|VE:Venezuela|VG:British Virgin Islands|VI:US Virgin Islands|"
        + "VN:Vietnam|VU:Vanuatu|WF:Wallis and Futuna|WS:Samoa|XK:Kosovo|YE:Yemen|YT:Mayotte|ZA:South Africa|"
        + "ZM:Zambia|ZW:Zimbabwe";

    /// <summary>Other names for a code, as players and the guide index write them.</summary>
    private static readonly (string Code, string[] Names)[] Aliases =
    [
        ("US", ["usa", "united states of america", "us"]),
        ("GB", ["uk", "great britain", "britain", "england", "scotland", "wales", "northern ireland"]),
        ("CZ", ["czech republic", "czech"]),
        ("TR", ["turkiye"]),
        ("RU", ["russian federation"]),
        ("KR", ["korea", "republic of korea"]),
        ("KP", ["dprk"]),
        ("MM", ["burma"]),
        ("CI", ["ivory coast"]),
        ("VN", ["viet nam"]),
        ("LA", ["lao"]),
        ("CD", ["drc", "democratic republic of the congo", "congo kinshasa"]),
        ("CG", ["congo", "congo brazzaville", "republic of congo"]),
        ("NL", ["holland", "the netherlands"]),
        ("PS", ["west bank", "israel west bank", "gaza", "palestinian territories"]),
        ("MO", ["macao"]),
        ("VA", ["vatican", "holy see"]),
        ("CV", ["cabo verde"]),
        ("TL", ["east timor", "timor leste"]),
        ("SZ", ["swaziland"]),
        ("MK", ["macedonia"]),
        ("BS", ["the bahamas"]),
        ("GM", ["the gambia"]),
        ("FK", ["falklands"]),
        ("FO", ["faroes"]),
        ("MP", ["nmi", "mariana islands"]),
        ("VI", ["united states virgin islands"]),
        ("AE", ["uae", "emirates"]),
        ("BA", ["bosnia"]),
        ("GS", ["south georgia", "south georgia sandwich islands"]),
        ("CC", ["cocos keeling islands"]),
        ("ST", ["sao tome"])
    ];

    private static readonly Dictionary<string, string> NameByCode = BuildNames();

    private static readonly Dictionary<string, string> CodeByName = BuildCodes();

    /// <summary>The country's English name, or the code itself when it is none the table knows ("ZZ").</summary>
    public static string NameOf(string code) =>
        NameByCode.TryGetValue(code.ToUpperInvariant(), out var name) ? name : code;

    /// <summary>Whether the code is a country's, as opposed to the database's "ZZ" for "unknown".</summary>
    public static bool IsCountry(string code) => NameByCode.ContainsKey(code.ToUpperInvariant());

    /// <summary>
    /// The code for a country the model named: a code itself ("MX"), a name or alias ("Mexico", "USA"),
    /// or anything the guides' gazetteer resolves to exactly one country (a typo, a region: "Sabah").
    /// Null when it names no country, or several ("Scandinavia").
    /// </summary>
    public static string? Resolve(string? country, CountryGazetteer? gazetteer = null)
    {
        if (string.IsNullOrWhiteSpace(country))
        {
            return null;
        }

        var trimmed = country.Trim();
        if (trimmed.Length == 2 && NameByCode.ContainsKey(trimmed.ToUpperInvariant()))
        {
            return trimmed.ToUpperInvariant();
        }

        if (CodeByName.TryGetValue(CountryGazetteer.Normalise(trimmed), out var code))
        {
            return code;
        }

        var mentions = gazetteer?.Resolve(trimmed) ?? [];
        return mentions.Count == 1 && CodeByName.TryGetValue(CountryGazetteer.Normalise(mentions[0].Name), out var resolved)
            ? resolved
            : null;
    }

    private static Dictionary<string, string> BuildNames() =>
        Table.Split('|')
            .Select(entry => entry.Split(':', 2))
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);

    private static Dictionary<string, string> BuildCodes()
    {
        var codes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (code, name) in NameByCode)
        {
            codes[CountryGazetteer.Normalise(name)] = code;
        }

        foreach (var (code, names) in Aliases)
        {
            foreach (var name in names)
            {
                codes[CountryGazetteer.Normalise(name)] = code;
            }
        }

        return codes;
    }
}
