namespace Configuration;

/// <summary>
/// How GeoGuessr's club activity feed awards club XP.
///
/// The feed labels every entry with a numeric <c>type</c>, which is the authoritative signal. Entries
/// that arrive without one (older captures, a stand-in, tests) are classified by their XP amount
/// through <see cref="UntypedXpFallback"/>. Since the mission board several sources are worth the
/// same 20 XP, so the map is empty by default: an untyped entry is then simply unknown.
/// </summary>
public class ClubXpConfiguration
{
    public const string SectionName = "ClubXp";

    /// <summary>
    /// XP amount → activity kind name (a <c>ClubXpActivityKind</c> member, e.g.
    /// <c>"BoardClearBonus"</c>) for feed entries without a type. Amounts not listed are unknown.
    /// </summary>
    public Dictionary<int, string> UntypedXpFallback { get; set; } = [];
}
