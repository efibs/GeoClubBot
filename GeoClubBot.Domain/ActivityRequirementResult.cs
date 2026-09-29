namespace Entities;

/// <summary>
/// How a member did against one of the club's weekly requirements.
/// </summary>
/// <param name="Kind">The activity kind counted; null for the rule-XP requirement.</param>
/// <param name="Actual">Entries of <paramref name="Kind"/> in the window, or the rule XP earned.</param>
/// <param name="Target">
/// What the member needed: <paramref name="Required"/>, scaled down for a member who joined during
/// the window or was excused, and 0 inside the grace period.
/// </param>
/// <param name="Required">The club's unscaled requirement.</param>
public sealed record ActivityRequirementResult(ClubXpActivityKind? Kind, int Actual, int Target, int Required)
{
    public bool Met => Actual >= Target;

    /// <summary>Short lower-case name for messages, e.g. <c>streak</c> or <c>missions</c>.</summary>
    public string Label => Kind?.ShortLabel() ?? "XP";
}
