using System.ComponentModel.DataAnnotations;

namespace Configuration;

/// <summary>
/// Descriptions written by a vision model for indexed guide pictures that have no words of their own.
///
/// Many guide pictures carry no caption and no prose around them, so a written question cannot find
/// them and an answer that shows one cannot say what it shows. A free vision model describes each such
/// picture once; the description is stored with the chunk, searched like a caption, and marked as
/// generated wherever it is shown, so it is never taken for the guide author's words.
/// </summary>
public class AiCaptionsConfiguration
{
    /// <summary>Nested under the AI section so the whole feature stays configurable from one place.</summary>
    public const string SectionName = "AI:Captions";

    /// <summary>Whether pictures without words are described at all. Off, nothing is asked of any model.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Share of the daily allowance describing pictures may spend, as a ceiling on the day's shared
    /// counter. Each picture costs one chat request, plus one embedding request per batch of
    /// descriptions. The job runs before indexing, so its share is claimed first; indexing then claims up
    /// to its own ceiling and answers have whatever is left.
    /// </summary>
    [Range(1, 100)]
    public int MaxDailyBudgetPercent { get; set; } = 20;

    /// <summary>Pictures described per run, whatever the allowance. Bounds how long one run holds the model slot.</summary>
    [Range(1, 10_000)]
    public int MaxPicturesPerRun { get; set; } = 60;

    /// <summary>
    /// Which generation of descriptions is current. A picture described under an older version is
    /// described again, so raising this re-captions the whole index over the following runs — after a
    /// prompt change, or once a better free vision model is around.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int Version { get; set; } = 1;

    /// <summary>Longest description kept, in characters. A model asked for two sentences sometimes writes a page.</summary>
    [Range(40, 2_000)]
    public int MaxCaptionLength { get; set; } = 400;

    /// <summary>
    /// How long one picture may take the model. A vision model is slower than a text one, and one
    /// stuck picture must not hold the run.
    /// </summary>
    [Range(5, 600)]
    public int TimeoutSeconds { get; set; } = 90;

    /// <summary>
    /// Whether a generated description is shown to members, under a picture whose guide says nothing
    /// about it. Off, descriptions only make pictures findable and tell the answering model what they
    /// show; the model's own answer can still describe the picture in its words.
    /// </summary>
    public bool ShowToMembers { get; set; } = true;

    /// <summary>
    /// Pictures that got an unusable reply in a row before the run stops: the model is not describing
    /// anything today, and spending the rest of the share on it would describe nothing.
    /// </summary>
    [Range(1, 100)]
    public int MaxConsecutiveFailures { get; set; } = 3;
}
