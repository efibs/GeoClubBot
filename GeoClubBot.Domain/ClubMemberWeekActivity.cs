namespace Entities;

/// <summary>
/// What a member earned club XP for on one day. There are two independent sources - the daily
/// mission, and playing the daily challenge or winning a duel - and a day only counts as fully
/// done when both happened.
/// </summary>
public record DayMissionStatus(DateOnly Date, bool MissionCompleted, bool ChallengeCompleted)
{
    public bool BothCompleted => MissionCompleted && ChallengeCompleted;

    public bool AnyCompleted => MissionCompleted || ChallengeCompleted;
}

public record ClubMemberWeekActivity(
    int TotalXp,
    IReadOnlyList<DayMissionStatus> DailyMissions,
    bool JoinedThisWeek,
    DateTimeOffset JoinedDateTime)
{
    /// <summary>Days on which the member earned both of the day's club-XP awards.</summary>
    public int NumDaysDone => DailyMissions.Count(d => d.BothCompleted);

    /// <summary>Days on which the member completed the daily mission, whatever else they did.</summary>
    public int NumMissionDaysDone => DailyMissions.Count(d => d.MissionCompleted);

    /// <summary>Days on which the member played the daily challenge or won a duel.</summary>
    public int NumChallengeDaysDone => DailyMissions.Count(d => d.ChallengeCompleted);

    public bool AllDaysCompleted => DailyMissions.Count > 0 && DailyMissions.All(d => d.BothCompleted);
}
