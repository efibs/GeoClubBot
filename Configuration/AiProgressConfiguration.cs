using System.ComponentModel.DataAnnotations;

namespace Configuration;

/// <summary>
/// What a member waiting for an answer is shown while it is made (#481): the steps the answer has taken
/// and is taking — searching the guides, looking up a plant, thinking. A question asked by message gets a
/// small reply under it that is edited as the steps run and deleted once the answer is posted; Discord
/// has no ephemeral reply to a message. <c>/ai review</c> shows them in its own ephemeral reply.
/// </summary>
public class AiProgressConfiguration
{
    public const string SectionName = "AI:Progress";

    public bool Enabled { get; set; } = true;

    /// <summary>How long an answer runs before its steps are shown; 0 shows them at once.</summary>
    [Range(0, 600)]
    public double ShowAfterSeconds { get; set; }

    /// <summary>
    /// The least time between two edits of the steps shown. Discord limits edits per channel, and the
    /// steps that run at once would otherwise edit several times a second.
    /// </summary>
    [Range(0.5, 60)]
    public double MinEditIntervalSeconds { get; set; } = 2;
}
