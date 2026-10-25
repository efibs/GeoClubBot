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
    /// Rounds of tool calls one answer may take. The call after the last round has to answer. Three
    /// leaves room to search, read the guide a search found, and search once more; each round costs at
    /// least one chat request and the time it takes the model to think again.
    /// </summary>
    [Range(1, 6)]
    public int MaxToolRounds { get; set; } = 3;

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

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
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
}
