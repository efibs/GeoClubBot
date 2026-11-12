using System.Text.RegularExpressions;

namespace UseCases.UseCases.AI.Tools;

/// <summary>
/// Reads what a question asks the whereabouts of: "where can I find Khasi pines?", "where does agave
/// lechuguilla grow?", "which countries have baobabs?", "where in the US can you see cholla?". The
/// names come back in the forms worth asking GBIF for, best first, with the one place the question is
/// about, when it names one.
///
/// Only the shape of the question is read here, never whether the thing is a plant: "where can I find
/// yellow bollards?" yields "yellow bollards", and the species database, which takes only names it can
/// stand behind, is what says no. So the rules aim at the phrase, not the topic.
///
/// A question about one place says so before the thing ("where in the US…") or after it ("…sugar cane
/// in the Philippines?"). The place is handed on as written; whether it is a country, and whether the
/// database knows enough of the plant there to be worth asking, is for the caller: within a country
/// GBIF counts where people happened to record a plant, not where it grows — 308 records of sugar cane
/// in the Philippines, 21 of them in the top province, and none of the top eight in Negros, which grows
/// most of it (measured 2026-10-30). Across the world the same counts do say where a plant is: Khasi
/// pine in the Philippines, China, Thailand, India, Vietnam and Myanmar, which is what a member asked
/// for and the guides did not have.
///
/// Pure, so every rule is pinned by a test.
/// </summary>
public static partial class PlantRangeQuestion
{
    /// <summary>Forms tried per question; each costs up to two GBIF reads when it is not a plant.</summary>
    private const int MaxNames = 3;

    /// <summary>
    /// Words that are not a name but a pointer to one ("where can I find this?") or the person asking
    /// ("where do they grow rice"). A question whose thing starts with one is not read.
    /// </summary>
    private static readonly HashSet<string> NotANameWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "this", "that", "these", "those", "it", "them", "they", "one", "ones", "something", "anything",
        "such", "which", "what", "he", "she", "i", "you", "we", "people", "players", "more", "other",
    };

    /// <summary>
    /// Words that only say what kind of plant it is, dropped as a last try: GBIF knows "khasi pine",
    /// not "khasi pine tree". "Grass" and "palm" are not among them: "pampas grass" is the name.
    /// Dropping one can leave a lone word that names a genus ("baobab trees" → Baobab) or only part of a
    /// common name: "dragon" is a common name of tarragon. So a lone word left this way is taken only by
    /// its scientific name (<see cref="PlantRangeName.ScientificOnly"/>).
    /// </summary>
    private static readonly HashSet<string> KindWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "tree", "trees", "plant", "plants", "bush", "bushes", "shrub", "shrubs", "flower", "flowers",
    };

    /// <summary>
    /// The names a question asks the whereabouts of across the world, best first; empty when it asks no
    /// such thing, or asks it about one place (<see cref="Read"/> says which).
    /// </summary>
    public static IReadOnlyList<PlantRangeName> NamesAskedAbout(string? question) =>
        Read(question) is { Place: null } asked ? asked.Names : [];

    /// <summary>
    /// What a question asks the whereabouts of, or null when it asks no such thing. The names are in
    /// the forms worth asking for, best first: as written with a binomial's epithet in lower case
    /// ("Agave lechuguilla": GBIF reads "Agave Lechuguilla" as the genus alone), in the singular ("khasi
    /// pine": the vernacular names are singular), and without a word like "tree".
    /// </summary>
    public static PlantRangeAsk? Read(string? question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return null;
        }

        foreach (var pattern in Patterns)
        {
            var match = pattern.Match(question);
            if (!match.Success)
            {
                continue;
            }

            var names = Forms(match.Groups["thing"].Value);
            if (names.Count == 0)
            {
                return null;
            }

            // Named before the thing ("where in the US…") or after it ("…in the Philippines?").
            var place = match.Groups["place"].Success
                ? match.Groups["place"].Value
                : PlaceAfter().Match(question[(match.Index + match.Length)..]) is { Success: true } after
                    ? after.Groups["place"].Value
                    : null;

            return new PlantRangeAsk(names, OnePlace(place));
        }

        return null;
    }

    /// <summary>The place as a name to look up, or null when it is none: the world, the wild, a continent, "other countries".</summary>
    private static string? OnePlace(string? place)
    {
        if (string.IsNullOrWhiteSpace(place))
        {
            return null;
        }

        var name = LeadingArticle().Replace(place.Trim(), string.Empty).Trim();
        return name.Length == 0 || NotOnePlace().IsMatch(name) ? null : name;
    }

    private static IReadOnlyList<PlantRangeName> Forms(string thing)
    {
        var words = thing.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0
            || NotANameWords.Contains(words[0])
            || words.Any(word => word.Any(char.IsDigit)))
        {
            return [];
        }

        // A binomial's epithet is lower case; a common name compares regardless of case.
        string[] written = [words[0], .. words.Skip(1).Select(word => word.ToLowerInvariant())];
        var withoutKind = written.Length > 1 && KindWords.Contains(written[^1]) ? written[..^1] : written;

        return new[]
            {
                new PlantRangeName(string.Join(' ', written), ScientificOnly: false),
                new PlantRangeName(string.Join(' ', Singular(written)), ScientificOnly: false),
                new PlantRangeName(string.Join(' ', Singular(withoutKind)), ScientificOnly: withoutKind.Length == 1 && written.Length > 1),
            }
            .DistinctBy(name => name.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxNames)
            .ToList();
    }

    /// <summary>The words with the last one in the singular, by the plain English rules plant names follow.</summary>
    private static string[] Singular(string[] words)
    {
        var last = words[^1];
        var lower = last.ToLowerInvariant();

        string singular;
        if (lower.EndsWith("ies", StringComparison.Ordinal) && last.Length > 4)
        {
            singular = last[..^3] + "y";
        }
        else if (lower.EndsWith("cti", StringComparison.Ordinal))
        {
            singular = last[..^1] + "us";
        }
        else if (lower.EndsWith("ches", StringComparison.Ordinal) || lower.EndsWith("shes", StringComparison.Ordinal)
                 || lower.EndsWith("sses", StringComparison.Ordinal) || lower.EndsWith("xes", StringComparison.Ordinal))
        {
            singular = last[..^2];
        }
        else if (lower.EndsWith('s') && !lower.EndsWith("ss", StringComparison.Ordinal)
                 && !lower.EndsWith("us", StringComparison.Ordinal) && !lower.EndsWith("is", StringComparison.Ordinal)
                 && last.Length > 3)
        {
            singular = last[..^1];
        }
        else
        {
            return words;
        }

        return [.. words[..^1], singular];
    }

    /// <summary>
    /// The thing asked about: up to four words, without an article, ending where the question goes on
    /// to say something else about it ("in Mexico", "outside India", "and why").
    /// </summary>
    private const string Thing =
        @"(?:(?:the|a|an|some|any)\s+)?(?<thing>\p{L}[\p{L}\p{M}'’-]*(?:\s+\p{L}[\p{L}\p{M}'’-]*){0,3}?)";

    private const string ThingEnds =
        @"(?=\s*(?:[?.!,;:)]|$)|\s+(?:in|on|at|near|around|outside|other|besides|apart|except|and|or|but|for|to|if|when"
        + @"|across|within|anywhere|elsewhere|naturally|wild|nowadays|today|please|exactly|mostly|usually|too|also|grow|growing)\b)";

    /// <summary>
    /// How such a question opens: "where", and perhaps the place it is about, "where in the US…".
    /// "Where in the world can I find…" and "where on earth…" ask about every country; the place is
    /// read all the same, and found to be none (<see cref="OnePlace"/>).
    /// </summary>
    private const string Where =
        @"\bwhere\s+(?:(?:in|within|inside)\s+(?<place>\p{L}[\p{L}\p{M} .'’-]{1,40}?)\s+|on\s+earth\s+)?(?:else\s+)?";

    private const string Adverb = @"(?:(?:also|usually|typically|normally|generally|still|actually|commonly)\s+)?";

    private static readonly Regex[] Patterns =
    [
        // "where can I find X", "where to see X", "where would you spot X", "where in the US do you get X"
        new(Where + @"(?:can|could|do|does|would|will|might|may|should|to)\s+(?:(?:i|you|we|one|people|players)\s+)?"
            + Adverb + @"(?:find|see|spot|encounter|expect|get)\s+" + Thing + ThingEnds,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),

        // "where do they grow X", "where do farmers plant X"
        new(Where + @"(?:do|does|did|can|could)\s+(?:they|people|farmers|you|we|i)\s+(?:grow|plant|farm|cultivate)\s+" + Thing + ThingEnds,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),

        // "where does X grow", "where do X occur"
        new(Where + @"(?:do|does|did|can|could|would|will)\s+" + Thing + @"\s+" + Adverb + @"(?:grow|occur|appear|live)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),

        // "where is X found", "where are X native"
        new(Where + @"(?:is|are|was|were)\s+" + Thing + @"\s+" + Adverb
            + @"(?:found|grown|growing|common|native|located|planted|seen|recorded|distributed)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),

        // "which countries have X", "in what countries can I find X"; "what countries does X grow in" is the next one's.
        new(@"\b(?:which|what)\s+countries\s+(?:\S+\s+){0,3}?(?:find|see|spot|have|has|grow|grows)\s+(?!in\b)" + Thing + ThingEnds,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),

        // "which countries is X found in", "what countries does X grow in"
        new(@"\b(?:which|what)\s+countries\s+(?:is|are|does|do)\s+" + Thing + @"\s+" + Adverb + @"(?:found|grown|grow|native|common)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
    ];

    /// <summary>What follows the thing says where: "in the Philippines", "only within Mexico".</summary>
    [GeneratedRegex(
        @"^\s*(?:only\s+)?(?:in|within|inside)\s+(?<place>\p{L}[\p{L}\p{M} .'’-]{1,40}?)\s*(?:[?.!,;:)]|$|\s+(?:and|or|but|if|when|please|too|also)\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlaceAfter();

    /// <summary>The world, the wild, other countries and continents are not one place.</summary>
    [GeneratedRegex(
        @"^(?:world|wild|earth|nature)\b|^other\s+countr|^(?:which|what|any|other|more|many|several|general)\b"
        + @"|^(?:europe|asia|africa|oceania|americas|(?:north|south|central|latin)\s+america|south\s*-?\s*east\s+asia)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NotOnePlace();

    [GeneratedRegex(@"^the\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LeadingArticle();
}

/// <param name="Name">A form of the name to ask the species database for.</param>
/// <param name="ScientificOnly">Take it only when it matches a scientific name, not a common one.</param>
public sealed record PlantRangeName(string Name, bool ScientificOnly);

/// <param name="Names">The forms of the name worth asking for, best first.</param>
/// <param name="Place">The one place the question is about, as it names it ("US", "Philippines"); null when it asks about the world.</param>
public sealed record PlantRangeAsk(IReadOnlyList<PlantRangeName> Names, string? Place);
