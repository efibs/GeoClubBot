using System.ComponentModel.DataAnnotations;

namespace Configuration;

/// <summary>
/// One lesser-known clue with its picture, posted to a channel on a schedule. The clues in the indexed
/// guides are otherwise seen only when someone happens to ask the right question; this puts one in
/// front of everyone. Drawn at random from the guides other than PlonkIt, the guide every player has
/// read, leaving out what PlonkIt has a tip like, and never one posted recently. Costs no AI request.
/// </summary>
public class AiClueOfTheDayConfiguration : IValidatableObject
{
    public const string SectionName = "AI:ClueOfTheDay";

    /// <summary>Off by default: a channel has to be chosen first.</summary>
    public bool Enabled { get; set; }

    /// <summary>The channel the clue is posted to.</summary>
    public ulong? TextChannelId { get; set; }

    /// <summary>Days before a clue may be posted again. The index holds thousands of picture clues, so a season is no strain.</summary>
    [Range(1, 3650)]
    public int NoRepeatDays { get; set; } = 180;

    /// <summary>
    /// Leave out clues PlonkIt has a tip like, as a question for "a clue I don't know" does. Off, a
    /// clue of the other guides is posted whether or not PlonkIt carries it too; PlonkIt's own tips are
    /// never drawn either way, since they are what every player has read.
    /// </summary>
    public bool ExcludeMainGuideClues { get; set; } = true;

    /// <summary>
    /// Clues drawn per post to choose from, in case the first were posted recently. Each draw is one
    /// random sample of the index, costing nothing but a moment.
    /// </summary>
    [Range(1, 50)]
    public int Draws { get; set; } = 8;

    /// <summary>What the post opens with. <c>{{guide}}</c> is the guide's title.</summary>
    [MinLength(1)]
    public string Heading { get; set; } = "🧭 **Clue of the day** — from *{{guide}}*";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Enabled && TextChannelId is null)
        {
            yield return new ValidationResult($"{nameof(TextChannelId)} is required while the clue of the day is enabled.");
        }
    }
}
