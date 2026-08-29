namespace UseCases.UseCases.AI.Ingestion;

/// <summary>
/// Labels a chunk with the kind of clue it describes, so the embedding carries a topic even when the
/// source publishes no useful structure of its own.
///
/// Measured against the embedding model on real guide text: a topical header is the single largest
/// influence on whether a question finds the right chunk. A source's own headings are the best
/// version of it, and where those exist this classifier is not needed — but a slide numbered "Slide 4"
/// or an album titled after its author carries no topic at all, and those are most of the library.
///
/// Deliberately keyword-based rather than a model call. The previous implementation classified every
/// chunk with an LLM, which is affordable against a self-hosted model and completely unaffordable
/// against a metered one — thousands of chunks would be thousands of requests from a daily allowance
/// of a few dozen. This costs nothing, is deterministic, and is re-runnable without spending anything.
/// </summary>
public static class ChunkTopicClassifier
{
    /// <summary>
    /// Ordered most specific first: a chunk mentioning both a bollard and a field is about
    /// infrastructure, and the first match wins.
    /// </summary>
    private static readonly (string Topic, string[] Keywords)[] Topics =
    [
        ("Google car and coverage",
            ["google car", "camera generation", "gen 2", "gen 3", "gen 4", "blurry", "blur", "snorkel",
             "roof rack", "antenna", "car blur", "rally car", "trekker", "coverage year", "photo sphere"]),

        ("Language and script",
            ["script", "alphabet", "cyrillic", "arabic script", "diacritic", "letter", "language",
             "spelling", "accent", "character", "writing system", "font"]),

        ("Road markings and signage",
            ["road marking", "centre line", "center line", "white line", "yellow line", "chevron",
             "road sign", "signpost", "stop sign", "give way", "speed limit", "kilometre marker",
             "milestone", "waystone", "route number", "road number"]),

        ("Infrastructure",
            ["bollard", "utility pole", "power line", "pylon", "guardrail", "guard rail", "barrier",
             "streetlight", "street light", "lamp post", "insulator", "crossarm", "cross arm",
             "manhole", "kerb", "curb", "fire hydrant", "telephone pole", "electricity"]),

        ("Vehicles and plates",
            ["licence plate", "license plate", "number plate", "plate", "bumper", "taxi", "bus",
             "truck", "vehicle", "car model", "registration"]),

        ("Architecture",
            ["roof", "building", "house", "architecture", "facade", "balcony", "window", "brick",
             "wall", "shutter", "tile", "veranda", "fence", "parapet", "minaret", "church", "temple"]),

        ("Agriculture",
            ["crop", "farm", "field", "orchard", "plantation", "olive", "rice", "paddy", "vineyard",
             "livestock", "cattle", "irrigation", "harvest", "greenhouse", "agriculture"]),

        ("Landscape and vegetation",
            ["mountain", "desert", "soil", "vegetation", "tree", "palm", "forest", "grass", "terrain",
             "climate", "coast", "river", "dune", "savanna", "conifer", "eucalyptus"]),

        ("Store chains and brands",
            ["chain", "supermarket", "petrol station", "gas station", "shop", "store", "brand",
             "franchise", "logo", "advertis"]),

        ("Regional clues",
            ["region", "province", "state", "governorate", "prefecture", "district", "county",
             "area code", "phone code", "postcode", "zip code", "domain", "canton", "oblast"]),

        ("Identifying the country",
            ["flag", "identify", "tell it apart", "distinguish", "confused with", "versus",
             "compared to", "narrow down"])
    ];

    /// <summary>
    /// The chunk's topic, or <c>null</c> when nothing matches confidently.
    ///
    /// Returning null rather than a catch-all label is deliberate: measurement showed that
    /// non-topical text in the header actively *lowers* similarity, so a meaningless
    /// "Miscellaneous" would be worse than saying nothing.
    /// </summary>
    public static string? Classify(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var haystack = text.ToLowerInvariant();

        foreach (var (topic, keywords) in Topics)
        {
            if (keywords.Any(keyword => haystack.Contains(keyword, StringComparison.Ordinal)))
            {
                return topic;
            }
        }

        return null;
    }
}
