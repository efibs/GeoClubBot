using System.ComponentModel.DataAnnotations;

namespace Configuration;

/// <summary>
/// The knowledge-gap report: which questions the index answers badly, so admins know which guides to
/// add. Questions with a weak retrieval are counted as they are asked (unrated conversations are swept
/// by retention, so they cannot be read back later); 👎-rated answers come from the feedback archive.
/// </summary>
public class AiKnowledgeGapsConfiguration : IValidatableObject
{
    public const string SectionName = "AI:KnowledgeGaps";

    /// <summary>Whether weak retrievals are recorded at all. Off, the report shows the rated answers only.</summary>
    public bool RecordWeakRetrievals { get; set; } = true;

    /// <summary>Days a recorded weak retrieval is kept.</summary>
    [Range(1, 3650)]
    public int RetentionDays { get; set; } = 90;

    /// <summary>The period the report covers, in days, unless the admin asks for another.</summary>
    [Range(1, 3650)]
    public int ReportDays { get; set; } = 30;

    /// <summary>Groups (country and topic) listed in the report, most questions first.</summary>
    [Range(1, 50)]
    public int MaxGroups { get; set; } = 15;

    /// <summary>Example questions shown per group.</summary>
    [Range(0, 10)]
    public int ExamplesPerGroup { get; set; } = 2;

    /// <summary>Post the report to <see cref="TextChannelId"/> on <c>AI:KnowledgeGapsSchedule</c>. Off by default: a channel has to be chosen.</summary>
    public bool PostReport { get; set; }

    public ulong? TextChannelId { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (PostReport && TextChannelId is null)
        {
            yield return new ValidationResult($"{nameof(TextChannelId)} is required while {nameof(PostReport)} is on.");
        }
    }
}
