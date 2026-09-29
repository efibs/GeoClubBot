namespace Entities;

public static class ClubXpActivityKindExtensions
{
    /// <summary>Short lower-case name for messages, e.g. "streak 4/6 · missions 1/2".</summary>
    public static string ShortLabel(this ClubXpActivityKind kind) => kind switch
    {
        ClubXpActivityKind.DailyChallengeOrDuel => "streak",
        ClubXpActivityKind.BoardMission => "missions",
        ClubXpActivityKind.BoardClearBonus => "board bonuses",
        ClubXpActivityKind.ClubChallengePlayed => "club challenges",
        ClubXpActivityKind.DailyMission => "daily missions",
        ClubXpActivityKind.WeeklyMission => "weekly missions",
        _ => "other"
    };
}
