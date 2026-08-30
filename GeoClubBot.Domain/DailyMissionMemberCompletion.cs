namespace Entities;

public class DailyMissionMemberCompletion : BaseEntity
{
    public int Id { get; private set; }

    public Guid ClubId { get; private set; }

    public string UserId { get; private set; } = string.Empty;

    public DateOnly Date { get; private set; }

    /// <summary>Daily-mission completions that day. GeoGuessr awards this at most once per day.</summary>
    public int CompletedCount { get; private set; }

    /// <summary>
    /// Daily challenges played / duels won that day - the second way to earn the day's club XP,
    /// which GeoGuessr introduced on 2026-08-25.
    ///
    /// Nullable because rows written before the bot tracked it carry no information either way:
    /// <c>null</c> means "not tracked", not "did not happen". Consumers that combine the two
    /// signals (the streak query) must treat <c>null</c> as satisfied, or every historical streak
    /// collapses the day this ships.
    /// </summary>
    public int? DailyChallengeCount { get; private set; }

    public static DailyMissionMemberCompletion Create(
        Guid clubId,
        string userId,
        DateOnly date,
        int completedCount,
        int? dailyChallengeCount = null)
    {
        return new DailyMissionMemberCompletion
        {
            ClubId = clubId,
            UserId = userId,
            Date = date,
            CompletedCount = completedCount,
            DailyChallengeCount = dailyChallengeCount
        };
    }

    private DailyMissionMemberCompletion()
    {
    }
}
