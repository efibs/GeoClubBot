namespace Entities;

public class DailyMissionMemberCompletion : BaseEntity
{
    public int Id { get; private set; }

    public Guid ClubId { get; private set; }

    public string UserId { get; private set; } = string.Empty;

    public DateOnly Date { get; private set; }

    public int CompletedCount { get; private set; }

    public static DailyMissionMemberCompletion Create(
        Guid clubId,
        string userId,
        DateOnly date,
        int completedCount)
    {
        return new DailyMissionMemberCompletion
        {
            ClubId = clubId,
            UserId = userId,
            Date = date,
            CompletedCount = completedCount
        };
    }

    private DailyMissionMemberCompletion()
    {
    }
}
