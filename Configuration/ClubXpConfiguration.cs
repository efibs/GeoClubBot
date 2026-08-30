using System.ComponentModel.DataAnnotations;

namespace Configuration;

/// <summary>
/// How GeoGuessr's club activity feed awards club XP.
///
/// The feed labels every entry with a numeric <c>type</c>, which is the authoritative signal; the
/// XP amounts here are only the fallback for entries that arrive without one (older captures, the
/// mock, tests). They are configurable because GeoGuessr has changed them before: on 2026-08-25 a
/// second 20 XP source appeared alongside the daily mission.
/// </summary>
public class ClubXpConfiguration
{
    public const string SectionName = "ClubXp";

    /// <summary>XP awarded for completing the daily mission (activity type 1). Once per day.</summary>
    [Range(1, int.MaxValue)]
    public int DailyMissionXpReward { get; set; } = 20;

    /// <summary>
    /// XP awarded for playing the daily challenge or winning a duel (activity type 4). Once per
    /// day, and worth the same as the daily mission — which is why the type is needed to tell
    /// them apart.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int DailyChallengeXpReward { get; set; } = 20;

    /// <summary>XP awarded for completing a weekly mission (activity type 2).</summary>
    [Range(1, int.MaxValue)]
    public int WeeklyMissionXpReward { get; set; } = 1000;
}
