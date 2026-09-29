namespace Entities;

/// <summary>
/// Why a club activity awarded XP. GeoGuessr's activity feed labels every entry with a numeric
/// <c>type</c>; these are the values observed on the live API (see
/// <c>Tools/GeoClubBot.ApiProbe/README.md</c>, "Known activity types").
///
/// The distinction matters because several sources are worth the same 20 XP, so the amount alone
/// cannot tell them apart. On 2026-09-23 GeoGuessr replaced the daily and weekly missions with the
/// weekly club mission board; types 1 and 2 only still appear in older feed entries and stored history.
/// </summary>
public enum ClubXpActivityKind
{
    /// <summary>An entry whose type GeoGuessr has not used before; counted towards raw XP only.</summary>
    Unknown = 0,

    /// <summary>
    /// Feed type 1 — the old daily mission was completed. 20 XP, at most once per day. Ended on
    /// 2026-09-23 with the introduction of the club mission board.
    /// </summary>
    DailyMission = 1,

    /// <summary>Feed type 2 — an old weekly mission was completed. 1000 XP. Ended on 2026-09-23.</summary>
    WeeklyMission = 2,

    /// <summary>
    /// Feed type 3 — a club challenge was played (carries the challenge token). Worth 0 XP, so it
    /// is not a sign of club-XP activity.
    /// </summary>
    ClubChallengePlayed = 3,

    /// <summary>
    /// Feed type 4 — the daily challenge or a duel was played. 20 XP, at most once per
    /// day. GeoGuessr does not separate the two, and for the bot's purposes they are one thing:
    /// extending the member's daily streak.
    /// </summary>
    DailyChallengeOrDuel = 4,

    /// <summary>
    /// Feed type 5 — a mission on the weekly club mission board was completed. 20 XP, credited to
    /// the member who claimed the mission only, even when others helped finish it.
    /// </summary>
    BoardMission = 5,

    /// <summary>
    /// Feed type 6 — a whole mission board was cleared. 100 XP, credited to whoever completed the
    /// board's last mission, so it says little about that member's own activity.
    /// </summary>
    BoardClearBonus = 6
}
