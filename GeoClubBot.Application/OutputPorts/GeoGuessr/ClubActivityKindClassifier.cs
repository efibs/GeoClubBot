using Configuration;
using Entities;
using Microsoft.Extensions.Options;

namespace UseCases.OutputPorts.GeoGuessr;

/// <summary>
/// Turns a club activity feed entry into the reason it awarded XP.
///
/// The single place that knows how GeoGuessr labels activities. Everything that used to ask
/// "is this entry worth exactly 20 XP?" asks this instead, because since 2026-08-25 that question
/// has two answers: the daily mission and the daily challenge / duel win are both worth 20.
///
/// Classification prefers the feed's own <c>type</c> field. When it is absent — a stand-in that
/// doesn't set it, or a fixture written before the field was known — it falls back to the
/// configured XP amounts, which reproduces the bot's historical behaviour rather than dropping
/// the entry.
/// </summary>
public sealed class ClubActivityKindClassifier(IOptions<ClubXpConfiguration> config)
{
    private readonly ClubXpConfiguration _config = config.Value;

    public ClubXpActivityKind Classify(ReadClubActivitiesItemDto activity)
    {
        if (activity.Type is { } type)
        {
            return Enum.IsDefined(typeof(ClubXpActivityKind), type)
                ? (ClubXpActivityKind)type
                : ClubXpActivityKind.Unknown;
        }

        // No type on the entry. The weekly reward is distinctive, and the daily mission is the
        // older of the two 20 XP sources, so an untyped 20 XP entry is read as a mission.
        if (activity.XpReward == _config.WeeklyMissionXpReward)
        {
            return ClubXpActivityKind.WeeklyMission;
        }

        if (activity.XpReward == _config.DailyMissionXpReward)
        {
            return ClubXpActivityKind.DailyMission;
        }

        return ClubXpActivityKind.Unknown;
    }

    /// <summary>The daily mission was completed.</summary>
    public bool IsDailyMission(ReadClubActivitiesItemDto activity) =>
        Classify(activity) == ClubXpActivityKind.DailyMission;

    /// <summary>The daily challenge was played, or a duel was won — the second daily XP source.</summary>
    public bool IsDailyChallenge(ReadClubActivitiesItemDto activity) =>
        Classify(activity) == ClubXpActivityKind.DailyChallengeOrDuel;

    /// <summary>A weekly mission, which the club-XP views report separately from daily activity.</summary>
    public bool IsWeeklyMission(ReadClubActivitiesItemDto activity) =>
        Classify(activity) == ClubXpActivityKind.WeeklyMission;
}
