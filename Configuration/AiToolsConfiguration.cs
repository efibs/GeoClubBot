using System.ComponentModel.DataAnnotations;

namespace Configuration;

/// <summary>
/// The tools the answering model may call: search the guides again in its own words, read more of a
/// guide it was given, and look up where a plant species is recorded.
///
/// Off the question's first call nothing changes: it carries the same excerpts it always did, and a
/// question the model answers straight away costs exactly the two requests it always has. Tools only
/// add to that — one chat request per further round, and one embedding request per round that
/// searches — and only while the day's allowance can afford them. When the model chain has no model
/// that can call tools, or the allowance is short, the question is answered the way it always was.
///
/// Measured on 2026-10-23/25 with the free models: nemotron-3-ultra, nemotron-3.5-super-vl,
/// nemotron-3.5-lightning and dots-3-note-preview all wrote well-formed tool calls and cited the
/// results in their answers; super-vl ran two searches at once when asked to compare two countries.
/// </summary>
public class AiToolsConfiguration : IValidatableObject
{
    /// <summary>Nested under the AI section so the whole feature stays configurable from one place.</summary>
    public const string SectionName = "AI:Tools";

    /// <summary>Offers the tools to models that can call them. Off answers every question in one call.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Rounds of tool calls one answer may take. The call after the last round has to answer; each
    /// round costs at least one chat request and the time it takes the model to think again.
    ///
    /// Two, measured on the answer replay of 2026-10-29 (49 rated questions, three rounds allowed): 11
    /// answers took a third round, and not one of them cited an excerpt it found. Six of those 11 then
    /// wrote a tool call as text instead of answering, and the rest cost 7–8 requests and 25–96 s. The
    /// second round did pay: five answers cited excerpts only it had found (#28, #30, #33, #35, #37).
    /// </summary>
    [Range(1, 6)]
    public int MaxToolRounds { get; set; } = 2;

    /// <summary>
    /// Tool calls run per round; calls beyond it are answered "not run". A round's searches share one
    /// embedding request, so this bounds the work a round does rather than its cost.
    /// </summary>
    [Range(1, 8)]
    public int MaxToolCallsPerRound { get; set; } = 4;

    /// <summary>
    /// Seconds from the question's arrival after which no more tools are offered: the next call has to
    /// answer. Calls already under way are not cut short. Ninety, because nemotron-3-ultra took up to
    /// 15 s per call on 2026-10-25 and a reader waiting a minute and a half is already waiting long.
    /// </summary>
    [Range(10, 600)]
    public int AnswerDeadlineSeconds { get; set; } = 90;

    /// <summary>Where a plant species is recorded. See <see cref="AiSpeciesRangeConfiguration"/>.</summary>
    public AiSpeciesRangeConfiguration SpeciesRange { get; set; } = new();

    /// <summary>Naming the plant in a member's screenshot with Pl@ntNet. See <see cref="AiPlantIdentificationConfiguration"/>.</summary>
    public AiPlantIdentificationConfiguration PlantIdentification { get; set; } = new();

    /// <summary>A map of the regions an answer names. See <see cref="AiRegionMapConfiguration"/>.</summary>
    public AiRegionMapConfiguration RegionMap { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var regionResults = new List<ValidationResult>();
        Validator.TryValidateObject(RegionMap, new ValidationContext(RegionMap), regionResults, validateAllProperties: true);

        foreach (var result in regionResults)
        {
            yield return new ValidationResult(
                $"{nameof(RegionMap)}: {result.ErrorMessage}",
                [.. result.MemberNames.Select(member => $"{nameof(RegionMap)}:{member}")]);
        }

        if (!Uri.TryCreate(RegionMap.BoundariesUrl, UriKind.Absolute, out _))
        {
            yield return new ValidationResult(
                $"{nameof(RegionMap)}:{nameof(AiRegionMapConfiguration.BoundariesUrl)} must be an absolute URL.");
        }

        var plantResults = new List<ValidationResult>();
        Validator.TryValidateObject(PlantIdentification, new ValidationContext(PlantIdentification), plantResults, validateAllProperties: true);

        foreach (var result in plantResults)
        {
            yield return new ValidationResult(
                $"{nameof(PlantIdentification)}: {result.ErrorMessage}",
                [.. result.MemberNames.Select(member => $"{nameof(PlantIdentification)}:{member}")]);
        }

        if (!Uri.TryCreate(PlantIdentification.BaseUrl, UriKind.Absolute, out _))
        {
            yield return new ValidationResult(
                $"{nameof(PlantIdentification)}:{nameof(AiPlantIdentificationConfiguration.BaseUrl)} must be an absolute URL.");
        }

        var location = PlantIdentification.Location;
        var locationResults = new List<ValidationResult>();
        Validator.TryValidateObject(location, new ValidationContext(location), locationResults, validateAllProperties: true);

        foreach (var result in locationResults)
        {
            yield return new ValidationResult(
                $"{nameof(PlantIdentification)}:{nameof(AiPlantIdentificationConfiguration.Location)}: {result.ErrorMessage}",
                [.. result.MemberNames.Select(member => $"{nameof(PlantIdentification)}:{nameof(AiPlantIdentificationConfiguration.Location)}:{member}")]);
        }

        if (!Uri.TryCreate(location.TownsUrl, UriKind.Absolute, out _))
        {
            yield return new ValidationResult(
                $"{nameof(PlantIdentification)}:{nameof(AiPlantIdentificationConfiguration.Location)}:{nameof(AiPlantLocationConfiguration.TownsUrl)} must be an absolute URL.");
        }

        // Nested sections are not validated by data annotations on their own.
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(SpeciesRange, new ValidationContext(SpeciesRange), results, validateAllProperties: true);

        foreach (var result in results)
        {
            yield return new ValidationResult(
                $"{nameof(SpeciesRange)}: {result.ErrorMessage}",
                [.. result.MemberNames.Select(member => $"{nameof(SpeciesRange)}:{member}")]);
        }

        if (!Uri.TryCreate(SpeciesRange.BaseUrl, UriKind.Absolute, out _))
        {
            yield return new ValidationResult(
                $"{nameof(SpeciesRange)}:{nameof(AiSpeciesRangeConfiguration.BaseUrl)} must be an absolute URL.");
        }

        var map = SpeciesRange.Map;
        var mapResults = new List<ValidationResult>();
        Validator.TryValidateObject(map, new ValidationContext(map), mapResults, validateAllProperties: true);

        foreach (var result in mapResults)
        {
            yield return new ValidationResult(
                $"{nameof(SpeciesRange)}:{nameof(AiSpeciesRangeConfiguration.Map)}: {result.ErrorMessage}",
                [.. result.MemberNames.Select(member => $"{nameof(SpeciesRange)}:{nameof(AiSpeciesRangeConfiguration.Map)}:{member}")]);
        }

        if (!Uri.TryCreate(map.TileBaseUrl, UriKind.Absolute, out _))
        {
            yield return new ValidationResult(
                $"{nameof(SpeciesRange)}:{nameof(AiSpeciesRangeConfiguration.Map)}:{nameof(AiSpeciesRangeMapConfiguration.TileBaseUrl)} must be an absolute URL.");
        }
    }
}

/// <summary>
/// The species-range tool, read from GBIF (the Global Biodiversity Information Facility): free, no key,
/// and counted per country or per region of one. It answers what the guides cannot — where a tree or a
/// crop grows — and costs no AI request.
/// </summary>
public class AiSpeciesRangeConfiguration
{
    /// <summary>Offers the tool. Off, the model is told of the other tools only.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Looks the plant up before the model answers when a question asks where in the world it is found
    /// ("where can I find Khasi pines?"), and offers its countries as an excerpt. Left to the model, the
    /// tool was called in none of 49 replayed answers (2026-10-30), Khasi pines included. Needs
    /// <see cref="Enabled"/>, but not tool calling: it costs no AI request.
    /// </summary>
    public bool LookUpWhereQuestions { get; set; } = true;

    /// <summary>GBIF's API. Changed only to point tests or a mirror elsewhere.</summary>
    public string BaseUrl { get; set; } = "https://api.gbif.org";

    /// <summary>Per request to GBIF. Its answers took under a second when measured; a hung one is retried once.</summary>
    [Range(1, 60)]
    public int RequestTimeoutSeconds { get; set; } = 10;

    /// <summary>How long an answer is kept. Records change slowly, and the same plant tends to be asked about in a row.</summary>
    [Range(0, 168)]
    public int CacheHours { get; set; } = 24;

    /// <summary>Countries, or regions of one, listed per answer, most records first.</summary>
    [Range(1, 30)]
    public int MaxAreas { get; set; } = 15;

    /// <summary>
    /// Fewest records a plant needs in one country before its records there are offered, with their
    /// map, for a question about that country ("where in the US can you see cholla?").
    ///
    /// Within a country the records follow who went looking more than where the plant grows, and a crop
    /// is hardly recorded at all: 308 records of sugar cane in the Philippines, none of the top eight
    /// provinces in Negros, which grows most of it (measured 2026-10-30). A plant people do record has
    /// thousands — the cholla genus has tens of thousands in the United States — so few records are
    /// taken to mean the database does not know, and the question is left to the guides.
    /// </summary>
    [Range(1, 1_000_000)]
    public int MinRecordsInCountry { get; set; } = 500;

    /// <summary>The map drawn of a plant's records. See <see cref="AiSpeciesRangeMapConfiguration"/>.</summary>
    public AiSpeciesRangeMapConfiguration Map { get; set; } = new();
}

/// <summary>
/// The map of where a plant is recorded, drawn from GBIF's own map tiles: its base map, and over it
/// the density of the species' records. Free and without a key, like the counts; the picture is kept in
/// the image relay, so it needs <c>AI:ImageRelay:PublicBaseUrl</c> to be set.
/// </summary>
public class AiSpeciesRangeMapConfiguration
{
    /// <summary>Draws the map. Off, a plant's range is offered in words alone.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>GBIF's base-map tile server. Changed only to point tests or a mirror elsewhere.</summary>
    public string TileBaseUrl { get; set; } = "https://tile.gbif.org";

    /// <summary>
    /// The base map's style. <c>osm-bright-en</c> draws state and province borders and names them in
    /// English, which is what makes a range readable as "southern Arizona, not Nevada"; GBIF's own
    /// <c>gbif-light</c> is calmer and names nothing.
    /// </summary>
    [Required]
    public string BaseMapStyle { get; set; } = "osm-bright-en";

    /// <summary>
    /// Seconds the whole map may take: a dozen tiles or so, fetched side by side. The answer waits for
    /// it, so past this the range is offered without a map.
    /// </summary>
    [Range(1, 60)]
    public int TimeoutSeconds { get; set; } = 8;
}

/// <summary>
/// Naming the plant in a member's screenshot with Pl@ntNet's identification API, before the model
/// answers. Free up to 500 identifications a day per account (reset at 00:00 UTC), commercial use
/// allowed within that, attribution appreciated and given in the excerpt's source. Asked to name a
/// plant from a screenshot, the free chat models guessed from look-alike guide pictures and were
/// often wrong (2026-11-09 feedback); Pl@ntNet names it with a confidence score, and the species it
/// names goes on to the GBIF range lookup, so the answer says what the plant is, where it grows, and
/// comes with the map.
/// </summary>
public class AiPlantIdentificationConfiguration
{
    /// <summary>Whether screenshots are sent to Pl@ntNet at all. Needs <see cref="ApiKey"/> as well.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// A Pl@ntNet API key (my.plantnet.org, free account). Empty leaves the feature off, however
    /// <see cref="Enabled"/> is set: nothing is sent anywhere without one.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Pl@ntNet's API. Changed only to point tests or a mirror elsewhere.</summary>
    public string BaseUrl { get; set; } = "https://my-api.plantnet.org";

    /// <summary>
    /// The flora the picture is matched against: "all" lets Pl@ntNet pick the most relevant project,
    /// which is what a screenshot of anywhere in the world needs. Other names come from /v2/projects.
    /// </summary>
    [MinLength(1)]
    public string Project { get; set; } = "all";

    /// <summary>Language of the common names Pl@ntNet returns (its /v2/languages).</summary>
    [MinLength(2)]
    public string Language { get; set; } = "en";

    /// <summary>
    /// Candidates offered to the model per screenshot, best first. The second and third say how sure
    /// the first is: a top score of 0.4 over 0.35 is a toss-up, over 0.05 it is the answer.
    /// </summary>
    [Range(1, 10)]
    public int MaxCandidates { get; set; } = 3;

    /// <summary>
    /// Score of the best candidate from which it is taken for the plant: its range is read from GBIF and
    /// offered with the map, and the model is told to name it. Below it the candidates are still offered,
    /// as guesses. Pl@ntNet's scores are probabilities over its flora; a Street View frame is a poor
    /// photograph of a plant, so a clear best match often scores 0.3–0.6 rather than 0.9.
    /// </summary>
    [Range(0.0, 1.0)]
    public double MinConfidence { get; set; } = 0.25;

    /// <summary>
    /// Identifications this bot makes a day, under Pl@ntNet's 500 so the account keeps room for the
    /// maintainer's own use and for a restart's lost count. Past it, members are told the day's
    /// allowance is spent rather than left to a silent guess.
    /// </summary>
    [Range(1, 500)]
    public int MaxRequestsPerDay { get; set; } = 450;

    /// <summary>Per request, the screenshot's download included. Pl@ntNet answers in a second or two.</summary>
    [Range(1, 60)]
    public int RequestTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// How long an identification is kept, by the screenshot's link. A follow-up on the same screenshot
    /// is asked often ("and where does it grow?"), and would otherwise cost another identification.
    /// </summary>
    [Range(0, 168)]
    public int CacheHours { get; set; } = 24;

    /// <summary>Largest screenshot sent, in bytes. Pl@ntNet takes up to 50 MB; a Discord screenshot is a few MB at most.</summary>
    [Range(100_000, 50_000_000)]
    public int MaxImageBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Candidates asked of Pl@ntNet per identification, of which <see cref="MaxCandidates"/> are offered
    /// to the model. The place a screenshot was taken re-orders them (see <see cref="Location"/>), so the
    /// plant that grows there has to be among them: on a hazy frame near Boston, eastern white pine was
    /// only 5th, at 1 % (2026-11-15). Pl@ntNet stops below 0.1 % on its own, whatever is asked; asking
    /// more costs no more identifications, only a slightly longer answer.
    /// </summary>
    [Range(1, 100)]
    public int CandidatesConsidered { get; set; } = 30;

    /// <summary>
    /// Pictures of one message sent together, as views of one plant: Pl@ntNet takes up to five per
    /// identification (a close-up, the bark, the whole tree) and judges them as one, for one
    /// identification of the day's allowance.
    /// </summary>
    [Range(1, 5)]
    public int MaxPictures { get; set; } = 5;

    /// <summary>
    /// Fewest image score a candidate needs before it is named as the plant when the place re-ordered
    /// the candidates. The place can lift a candidate the picture barely supports to the top; it is then
    /// offered as the likeliest of an uncertain lot, not as an identification. Without a place,
    /// <see cref="MinConfidence"/> alone decides.
    /// </summary>
    [Range(0.0, 1.0)]
    public double MinImageScore { get; set; } = 0.05;

    /// <summary>Weighing the candidates by where the screenshot was taken. See <see cref="AiPlantLocationConfiguration"/>.</summary>
    public AiPlantLocationConfiguration Location { get; set; } = new();
}

/// <summary>
/// Where a screenshot was taken, read from what the member wrote ("just south of Boston", "this is in
/// Bulgaria", a Google Maps link), and what is recorded there. Pl@ntNet sees only the picture, and its
/// world flora leans towards Europe, where most of its photographs come from: a red pine (North
/// American, never recorded in Bulgaria) topped a Bulgarian screenshot of black pines (2026-11-15).
/// Each candidate is weighed by how often GBIF records it near the place, so what grows there rises and
/// what is never recorded there falls. Pl@ntNet's own regional floras were not taken instead: they
/// filter the same scores, and they leave out what is only planted — no eucalyptus in Brazil, no
/// Monterey pine in New Zealand, clues a player most needs (measured 2026-11-15).
///
/// Free: GBIF needs no key, and the place names come from GeoNames' cities (CC BY 4.0) and Natural
/// Earth's states and provinces (public domain), each downloaded once.
/// </summary>
public class AiPlantLocationConfiguration
{
    /// <summary>Reads the place from the conversation and weighs the candidates by it. Off, Pl@ntNet's order stands.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Kilometres around a town or a pair of coordinates in which records count as "near". A member
    /// says "just south of Boston", not where exactly; fifty takes in the town and the country around it.
    /// A state or province counts its whole area, a country the whole country.
    /// </summary>
    [Range(1, 1000)]
    public int RadiusKm { get; set; } = 50;

    /// <summary>
    /// How strongly the records near the place weigh against the picture: each candidate's image score
    /// is multiplied by (records + 1) to this power. 0 ignores them; 1 trusts them as much as the
    /// picture. 0.5 put eastern white pine first on a hazy frame near Boston (7,269 records within
    /// 50 km against 209 for American elm, the picture's 1 % favourite), and native black and Scots pine
    /// over a planted white pine in Bulgaria, while a 52 % Norway spruce stayed first (2026-11-15).
    /// </summary>
    [Range(0.0, 2.0)]
    public double RecordsWeight { get; set; } = 0.5;

    /// <summary>
    /// What a candidate's score is multiplied by when GBIF has no record of it anywhere in the country:
    /// it most likely does not grow there. Not zero, because a well-photographed plant the records miss
    /// still has its picture to speak for it.
    /// </summary>
    [Range(0.0, 1.0)]
    public double UnrecordedFactor { get; set; } = 0.1;

    /// <summary>
    /// GeoNames' towns of 15,000 people or more (CC BY 4.0, about 3 MB), which "near Quincy" and "south
    /// of Boston" are matched against. Downloaded once into <see cref="AiRegionMapConfiguration.DataDirectory"/>;
    /// delete the file to fetch it again.
    /// </summary>
    public string TownsUrl { get; set; } = "https://download.geonames.org/export/dump/cities15000.zip";

    /// <summary>Seconds the one-off download of the towns may take.</summary>
    [Range(10, 3600)]
    public int DownloadTimeoutSeconds { get; set; } = 120;
}

/// <summary>
/// A small map of a country with the first-level regions an answer names highlighted — "far
/// north-eastern Vietnam: Lạng Sơn and Quảng Ninh" means little to a player who does not know where
/// those are. Boundaries come from Natural Earth's admin-1 states and provinces (public domain),
/// downloaded once; the base map is GBIF's, as for the species maps. Drawn after the answer, from the
/// region names in it, and also on the model's request through the <c>region_map</c> tool, which can
/// shade regions by how common a clue is in each.
/// </summary>
public class AiRegionMapConfiguration
{
    /// <summary>Whether maps are drawn at all. Off, nothing is downloaded either.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether the model may ask for a map itself (<c>region_map</c>), with a share per region — darker
    /// where a clue is more common. The map after the answer needs no call and highlights evenly.
    /// </summary>
    public bool ToolEnabled { get; set; } = true;

    /// <summary>
    /// Natural Earth's 1:10m admin-1 states and provinces as GeoJSON (about 40 MB, public domain). The
    /// 1:50m file covers only a dozen large countries, which is why the full one is taken. Downloaded
    /// once into <see cref="DataDirectory"/>; delete the file to fetch it again.
    /// </summary>
    public string BoundariesUrl { get; set; } =
        "https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_10m_admin_1_states_provinces.geojson";

    /// <summary>Where the boundaries file is kept, like the image relay's pictures. Relative to the working directory.</summary>
    [MinLength(1)]
    public string DataDirectory { get; set; } = "ai-data";

    /// <summary>
    /// How far, in degrees, a boundary may be straightened when the file is read (Douglas–Peucker). A
    /// country map is a few pixels per hundredth of a degree, so a hundredth is invisible and cuts the
    /// points kept in memory by an order of magnitude.
    /// </summary>
    [Range(0.0, 1.0)]
    public double SimplifyToleranceDegrees { get; set; } = 0.01;

    /// <summary>Most regions highlighted on one map. Past it the map is a country coloured in, and says nothing.</summary>
    [Range(1, 60)]
    public int MaxRegions { get; set; } = 15;

    /// <summary>Seconds the map after an answer may take; past this the answer goes out without it.</summary>
    [Range(1, 60)]
    public int TimeoutSeconds { get; set; } = 8;

    /// <summary>How long a drawn map is kept, by its country and regions. The same regions come up again and again.</summary>
    [Range(0, 168)]
    public int CacheHours { get; set; } = 24;

    /// <summary>Seconds the one-off download of the boundaries file may take.</summary>
    [Range(10, 3600)]
    public int DownloadTimeoutSeconds { get; set; } = 300;
}
