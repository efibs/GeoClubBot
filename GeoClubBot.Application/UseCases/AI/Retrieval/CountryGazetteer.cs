using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace UseCases.UseCases.AI.Retrieval;

/// <param name="Name">The country as it is best known, e.g. "united states".</param>
/// <param name="IndexNames">Every spelling the index files it under, e.g. "usa" and "united states".</param>
public sealed record CountryMention(string Name, IReadOnlyList<string> IndexNames);

/// <summary>
/// Finds the countries a question is about, as the index spells them.
///
/// Built from the index's own country names, because those are what a country search has to match —
/// and they are inconsistent: the same place is filed as <c>usa</c> (1093 chunks) and
/// <c>united states</c> (178), <c>czechia</c> (141) and <c>czech republic</c> (56), and
/// <c>são tomé &amp; príncipe</c> beside <c>sao tome and principe</c>. Every spelling of a country is
/// therefore one entry, found by any of them, by its common short forms ("UK", "UAE"), by the
/// adjective players write ("Ghanaian bollards", "Swedish snow poles"), by a typo one letter off
/// ("Philippenes", written so twice in one feedback export), and by a few regions that sit wholly in
/// one country or span a fixed few ("Sabah", "Scandinavia").
///
/// Only countries the index actually holds are ever named, so a mention can always be searched for.
/// Pure: the index's names come in, mentions come out.
/// </summary>
public sealed partial class CountryGazetteer
{
    /// <summary>
    /// A typo is only forgiven in a name this long. Shorter ones are too easily another word one letter
    /// off: "while" for Chile, "green" for Greek, "walls" and "sales" for Wales.
    /// </summary>
    private const int MinFuzzyLength = 7;

    /// <summary>
    /// Other names for a country, keyed by the name its group is known by. A group is merged with every
    /// index spelling any of its names normalises to, so <c>usa</c> and <c>united states</c> become
    /// one country whichever the index happens to hold.
    /// </summary>
    private static readonly (string Country, string[] Names)[] Synonyms =
    [
        // Not "american": "South American" is no question about the United States.
        ("united states", ["usa", "united states of america"]),
        ("czechia", ["czech republic", "czech"]),
        ("united kingdom", ["uk", "great britain", "britain", "british", "england", "scotland", "wales", "northern ireland", "scottish", "welsh"]),
        ("northern mariana islands", ["nmi", "mariana islands"]),
        ("israel west bank", ["west bank", "palestine"]),
        ("united arab emirates", ["uae", "emirates", "emirati"]),
        ("south korea", ["korea", "korean"]),
        ("north macedonia", ["macedonia", "macedonian"]),
        ("eswatini", ["swaziland"]),
        ("turkey", ["turkiye", "turkish"]),
        ("netherlands", ["holland", "dutch"]),
        ("macau", ["macao"]),
        ("cocos islands", ["cocos keeling islands"]),
        ("faroe islands", ["faroes", "faroese"]),
        ("falkland islands", ["falklands"]),
        ("south georgia sandwich islands", ["south georgia", "south georgia and the south sandwich islands"]),
        ("us virgin islands", ["united states virgin islands"]),
        ("new zealand", ["nz", "new zealander"]),
        ("brazil", ["brazilian"]),
        ("mexico", ["mexican"]),
        ("argentina", ["argentinian", "argentine"]),
        ("peru", ["peruvian"]),
        ("chile", ["chilean"]),
        ("colombia", ["colombian"]),
        ("ecuador", ["ecuadorian"]),
        ("bolivia", ["bolivian"]),
        ("uruguay", ["uruguayan"]),
        ("paraguay", ["paraguayan"]),
        ("panama", ["panamanian"]),
        ("guatemala", ["guatemalan"]),
        ("costa rica", ["costa rican"]),
        ("canada", ["canadian"]),
        ("australia", ["australian", "aussie"]),
        ("japan", ["japanese"]),
        ("thailand", ["thai"]),
        // Misspelt two letters off as often as one, which the typo rule does not reach.
        ("philippines", ["philippine", "filipino", "phillipines", "philipines", "phillippines"]),
        ("indonesia", ["indonesian"]),
        ("malaysia", ["malaysian"]),
        ("vietnam", ["vietnamese", "viet nam"]),
        ("cambodia", ["cambodian"]),
        ("laos", ["laotian", "lao"]),
        ("india", ["indian"]),
        ("nepal", ["nepali", "nepalese"]),
        ("bangladesh", ["bangladeshi"]),
        ("sri lanka", ["sri lankan"]),
        ("bhutan", ["bhutanese"]),
        ("pakistan", ["pakistani"]),
        ("mongolia", ["mongolian"]),
        ("kazakhstan", ["kazakh", "kazakhstani"]),
        ("kyrgyzstan", ["kyrgyz"]),
        ("russia", ["russian", "russian federation"]),
        ("ukraine", ["ukrainian"]),
        ("poland", ["polish"]),
        ("slovakia", ["slovak", "slovakian"]),
        ("slovenia", ["slovenian", "slovene"]),
        ("croatia", ["croatian"]),
        ("serbia", ["serbian"]),
        ("hungary", ["hungarian"]),
        ("romania", ["romanian"]),
        ("bulgaria", ["bulgarian"]),
        ("greece", ["greek"]),
        ("albania", ["albanian"]),
        ("montenegro", ["montenegrin"]),
        ("germany", ["german"]),
        ("france", ["french"]),
        ("spain", ["spanish"]),
        ("portugal", ["portuguese"]),
        ("italy", ["italian"]),
        ("switzerland", ["swiss"]),
        ("austria", ["austrian"]),
        ("belgium", ["belgian"]),
        ("luxembourg", ["luxembourgish"]),
        ("denmark", ["danish"]),
        ("sweden", ["swedish"]),
        ("norway", ["norwegian"]),
        ("finland", ["finnish"]),
        ("iceland", ["icelandic"]),
        ("estonia", ["estonian"]),
        ("latvia", ["latvian"]),
        ("lithuania", ["lithuanian"]),
        ("ireland", ["irish"]),
        ("malta", ["maltese"]),
        ("cyprus", ["cypriot"]),
        ("israel", ["israeli"]),
        ("jordan", ["jordanian"]),
        ("lebanon", ["lebanese"]),
        ("qatar", ["qatari"]),
        ("oman", ["omani"]),
        ("egypt", ["egyptian"]),
        ("tunisia", ["tunisian"]),
        ("kenya", ["kenyan"]),
        ("uganda", ["ugandan"]),
        ("rwanda", ["rwandan"]),
        ("tanzania", ["tanzanian"]),
        ("nigeria", ["nigerian"]),
        ("ghana", ["ghanaian"]),
        ("senegal", ["senegalese"]),
        ("south africa", ["south african"]),
        ("namibia", ["namibian"]),
        ("botswana", ["botswanan"]),
        ("lesotho", ["basotho"]),
        ("madagascar", ["malagasy"]),
        ("taiwan", ["taiwanese"]),
        ("china", ["chinese"]),
        ("singapore", ["singaporean"]),
        ("greenland", ["greenlandic"]),
        ("dominican republic", ["dominican"])
    ];

    /// <summary>
    /// Places that are not countries but sit wholly in some, so a question naming one is about them.
    /// Kept to places players name and the index files under their country rather than on their own;
    /// <c>AI:Retrieval:CountryBoost:Aliases</c> adds more.
    /// </summary>
    private static readonly (string Place, string[] Countries)[] Regions =
    [
        // Finland too, as players use the word: "snow poles in Scandinavia" is answered by a Finnish
        // guide (finland#7DPm), which a boost of the three kingdoms alone pushed from 2nd to 5th.
        ("scandinavia", ["norway", "sweden", "denmark", "finland"]),
        ("scandinavian", ["norway", "sweden", "denmark", "finland"]),
        ("nordic", ["norway", "sweden", "finland", "denmark", "iceland"]),
        ("nordics", ["norway", "sweden", "finland", "denmark", "iceland"]),
        ("baltic", ["estonia", "latvia", "lithuania"]),
        ("baltics", ["estonia", "latvia", "lithuania"]),
        ("benelux", ["belgium", "netherlands", "luxembourg"]),
        ("iberia", ["spain", "portugal"]),
        ("iberian", ["spain", "portugal"]),
        ("borneo", ["malaysia", "indonesia"]),
        ("sabah", ["malaysia"]),
        ("sarawak", ["malaysia"]),
        ("java", ["indonesia"]),
        ("sumatra", ["indonesia"]),
        ("bali", ["indonesia"]),
        ("sulawesi", ["indonesia"]),
        ("kyushu", ["japan"]),
        ("hokkaido", ["japan"]),
        ("okinawa", ["japan"]),
        ("tasmania", ["australia"]),
        ("patagonia", ["argentina", "chile"]),
        ("siberia", ["russia"]),
        ("baja california", ["mexico"]),
        // Regions whose names hold another country's, or a typo of one: the longer name wins.
        ("new mexico", ["united states"]),
        ("new jersey", ["united states"]),
        ("new south wales", ["australia"]),
        ("british columbia", ["canada"])
    ];

    /// <summary>Country values the index uses for things that are not places.</summary>
    private static readonly HashSet<string> NotPlaces = new(StringComparer.Ordinal) { "spillover countries", "beginners guide" };

    private readonly Dictionary<string, CountryMention[]> _byName;
    private readonly Dictionary<string, CountryMention[]> _byFuzzyWord;
    private readonly int _longestName;

    private CountryGazetteer(Dictionary<string, CountryMention[]> byName)
    {
        _byName = byName;
        _longestName = byName.Count == 0 ? 0 : byName.Keys.Max(name => name.Split(' ').Length);

        // Typos are only forgiven against single-word names, and only for words long enough that one
        // letter off is still unmistakably that name.
        _byFuzzyWord = byName
            .Where(entry => !entry.Key.Contains(' ', StringComparison.Ordinal) && entry.Key.Length >= MinFuzzyLength)
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
    }

    /// <summary>A gazetteer that knows no country, for when the index's names could not be read.</summary>
    public static CountryGazetteer Empty { get; } = new([]);

    /// <summary>Every name this gazetteer recognises, normalised. For tests and diagnostics.</summary>
    public IReadOnlyCollection<string> Names => _byName.Keys;

    /// <param name="indexCountries">The country values the index holds, as it spells them.</param>
    /// <param name="aliases">More names, each for one or more countries: configuration's additions.</param>
    public static CountryGazetteer Build(
        IEnumerable<string> indexCountries,
        IReadOnlyDictionary<string, List<string>>? aliases = null)
    {
        ArgumentNullException.ThrowIfNull(indexCountries);

        // Index spellings, grouped by the country they name.
        var spellings = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var synonymOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (country, names) in Synonyms)
        {
            synonymOf[Normalise(country)] = country;
            foreach (var name in names)
            {
                synonymOf[Normalise(name)] = country;
            }
        }

        foreach (var spelling in indexCountries.Where(spelling => !string.IsNullOrWhiteSpace(spelling)))
        {
            var normalised = Normalise(spelling);
            if (normalised.Length == 0 || NotPlaces.Contains(normalised))
            {
                continue;
            }

            var country = synonymOf.GetValueOrDefault(normalised, normalised);
            if (!spellings.TryGetValue(country, out var group))
            {
                spellings[country] = group = [];
            }

            var indexName = spelling.Trim().ToLowerInvariant();
            if (!group.Contains(indexName, StringComparer.Ordinal))
            {
                group.Add(indexName);
            }
        }

        var mentions = spellings.ToDictionary(
            entry => entry.Key,
            entry => new CountryMention(entry.Key, [.. entry.Value.Order(StringComparer.Ordinal)]),
            StringComparer.Ordinal);

        var byName = new Dictionary<string, CountryMention[]>(StringComparer.Ordinal);

        void Add(string name, IEnumerable<string> countries)
        {
            var key = Normalise(name);
            var found = countries
                .Select(country => synonymOf.GetValueOrDefault(Normalise(country), Normalise(country)))
                .Where(mentions.ContainsKey)
                .Select(country => mentions[country])
                .Distinct()
                .ToArray();

            if (key.Length > 0 && found.Length > 0)
            {
                byName[key] = found;
            }
        }

        foreach (var (country, mention) in mentions)
        {
            Add(country, [country]);
            foreach (var spelling in mention.IndexNames)
            {
                Add(spelling, [country]);
            }
        }

        foreach (var (country, names) in Synonyms)
        {
            foreach (var name in names)
            {
                Add(name, [country]);
            }
        }

        foreach (var (place, countries) in Regions)
        {
            Add(place, countries);
        }

        // Configured names last, so they can correct a built-in one.
        foreach (var (name, countries) in aliases ?? new Dictionary<string, List<string>>())
        {
            Add(name, countries);
        }

        return new CountryGazetteer(byName);
    }

    /// <summary>
    /// Countries named in <paramref name="text"/>, in order of first mention, each once. The longest
    /// name wins, so "New South Wales" is Australia and "Northern Ireland" the United Kingdom.
    /// </summary>
    public IReadOnlyList<CountryMention> FindMentions(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || _byName.Count == 0)
        {
            return [];
        }

        // "US" only in capitals, where it cannot be the pronoun.
        var words = Normalise(UnitedStatesAbbreviation().Replace(text, " united states ")).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var found = new List<CountryMention>();

        for (var start = 0; start < words.Length;)
        {
            var matched = 0;
            for (var length = Math.Min(_longestName, words.Length - start); length > 0; length--)
            {
                if (_byName.TryGetValue(string.Join(' ', words, start, length), out var countries))
                {
                    found.AddRange(countries);
                    matched = length;
                    break;
                }
            }

            if (matched == 0 && FindByTypo(words[start]) is { } misspelt)
            {
                found.AddRange(misspelt);
                matched = 1;
            }

            start += Math.Max(1, matched);
        }

        return [.. found.Distinct()];
    }

    /// <summary>
    /// The countries a name stands for — "USA", "Czech Republic", "Sabah" — or none. Matched as a whole
    /// name first, then as text, so "Malaysia (Sabah)" still resolves.
    /// </summary>
    public IReadOnlyList<CountryMention> Resolve(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? []
            : _byName.TryGetValue(Normalise(UnitedStatesAbbreviation().Replace(name, " united states ")), out var countries)
                ? countries
                : FindMentions(name);

    /// <summary>The single country a word is one typo away from, or null when it is none or several.</summary>
    private CountryMention[]? FindByTypo(string word)
    {
        if (word.Length < MinFuzzyLength)
        {
            return null;
        }

        CountryMention[]? best = null;
        foreach (var (name, countries) in _byFuzzyWord)
        {
            if (Math.Abs(name.Length - word.Length) > 1 || !IsOneEditApart(name, word))
            {
                continue;
            }

            // Two names of the same country one letter off ("philippine", "philippines") are one match.
            if (best is not null && !best.SequenceEqual(countries))
            {
                // Two countries' names one letter off: the word could be either, so it is neither.
                return null;
            }

            best = countries;
        }

        return best;
    }

    /// <summary>One insertion, deletion, substitution or swap of neighbours — the common typos.</summary>
    private static bool IsOneEditApart(string a, string b)
    {
        if (a == b)
        {
            return false;
        }

        if (a.Length == b.Length)
        {
            var differences = Enumerable.Range(0, a.Length).Where(index => a[index] != b[index]).ToList();
            return differences.Count == 1
                   || (differences.Count == 2
                       && differences[1] == differences[0] + 1
                       && a[differences[0]] == b[differences[1]]
                       && a[differences[1]] == b[differences[0]]);
        }

        var (shorter, longer) = a.Length < b.Length ? (a, b) : (b, a);
        var skip = 0;
        while (skip < shorter.Length && shorter[skip] == longer[skip])
        {
            skip++;
        }

        return shorter.AsSpan(skip).SequenceEqual(longer.AsSpan(skip + 1));
    }

    /// <summary>
    /// Lower case, accents folded and punctuation dropped, so "São Tomé &amp; Príncipe", "Sao Tome and
    /// Principe" and "Curaçao" compare the way a reader would.
    /// </summary>
    internal static string Normalise(string text)
    {
        var decomposed = text.Replace("&", " and ", StringComparison.Ordinal).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            // Every other mark separates words, the apostrophe included: "Italy's" is "italy s".
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    [GeneratedRegex(@"\bU\.?S\.?(?:A\.?)?(?![a-zA-Z])", RegexOptions.CultureInvariant)]
    private static partial Regex UnitedStatesAbbreviation();
}
