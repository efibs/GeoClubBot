using System.ComponentModel.DataAnnotations;

namespace Configuration;

/// <summary>
/// The report posted after the weekly check that suggests which members of the second club(s)
/// should swap with members of the main club. The bot never moves anyone; admins act on it.
/// </summary>
public class SwapSuggestionsConfiguration
{
    public const string SectionName = "SwapSuggestions";

    public bool Enabled { get; set; }

    /// <summary>Channel of the report. Null posts it to the activity checker's channel.</summary>
    public ulong? TextChannelId { get; set; }

    /// <summary>Weeks the rule-XP average is taken over.</summary>
    [Range(1, 52)]
    public int HistoryDepth { get; set; } = 2;

    /// <summary>
    /// How much higher a second-club member's average must be than a main-club member's for a swap.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int BufferXp { get; set; } = 10;

    /// <summary>GeoGuessr's club size limit. Fewer main-club members means free spots to promote into.</summary>
    [Range(1, 1000)]
    public int MaxClubSize { get; set; } = 30;

    /// <summary>Also suggest replacements for main-club members who ran out of strikes this week.</summary>
    public bool SuggestReplacementsForKicks { get; set; } = true;

    /// <summary>Also suggest promotions into free main-club spots.</summary>
    public bool SuggestPromotions { get; set; } = true;
}
