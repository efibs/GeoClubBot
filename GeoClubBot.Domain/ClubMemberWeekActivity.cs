namespace Entities;

public record DayMissionStatus(DateOnly Date, bool MissionCompleted);

public record ClubMemberWeekActivity(
    int TotalXp,
    IReadOnlyList<DayMissionStatus> DailyMissions,
    bool JoinedThisWeek,
    DateTimeOffset JoinedDateTime)
{
    public int NumDaysDone => DailyMissions.Count(d => d.MissionCompleted);
    public bool AllDaysCompleted => DailyMissions.Count > 0 && DailyMissions.All(d => d.MissionCompleted);
}