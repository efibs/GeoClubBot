namespace Entities;

/// <summary>
/// Why a club activity awarded XP. GeoGuessr's activity feed labels every entry with a numeric
/// <c>type</c>; these are the values observed on the live API (see
/// <c>Tools/GeoClubBot.ApiProbe/README.md</c>, "Known activity types").
///
/// The distinction matters because <see cref="DailyMission"/> and <see cref="DailyChallengeOrDuel"/>
/// are both worth 20 XP, so the amount alone cannot tell them apart. Before 2026-08-25 the daily
/// mission was the only 20 XP source, which is the assumption the bot used to make.
/// </summary>
public enum ClubXpActivityKind
{
    /// <summary>An entry whose type GeoGuessr has not used before; counted towards raw XP only.</summary>
    Unknown = 0,

    /// <summary>Feed type 1 — the daily mission was completed. 20 XP, at most once per day.</summary>
    DailyMission = 1,

    /// <summary>Feed type 2 — a weekly mission was completed. 1000 XP.</summary>
    WeeklyMission = 2,

    /// <summary>
    /// Feed type 3 — a club challenge was played (carries the challenge token). Worth 0 XP, so it
    /// is not a sign of club-XP activity.
    /// </summary>
    ClubChallengePlayed = 3,

    /// <summary>
    /// Feed type 4 — the daily challenge was played or a duel was won. 20 XP, at most once per
    /// day. GeoGuessr does not separate the two, and for the bot's purposes they are one thing:
    /// the second way to earn the day's club XP.
    /// </summary>
    DailyChallengeOrDuel = 4
}
