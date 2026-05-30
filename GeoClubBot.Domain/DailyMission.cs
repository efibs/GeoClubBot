namespace Entities;

public class DailyMission : BaseEntity
{
    public int Id { get; private set; }

    public Guid MissionId { get; private set; }

    public string Type { get; private set; } = string.Empty;

    public string GameMode { get; private set; } = string.Empty;

    public int CurrentProgress { get; private set; }

    public int TargetProgress { get; private set; }

    public bool Completed { get; private set; }

    public DateTimeOffset EndDate { get; private set; }

    public int RewardAmount { get; private set; }

    public string RewardType { get; private set; } = string.Empty;

    public string? MapSlug { get; private set; }

    public string? MapName { get; private set; }

    public DateTimeOffset FetchedAtUtc { get; private set; }

    public static DailyMission Create(
        Guid missionId,
        string type,
        string gameMode,
        int currentProgress,
        int targetProgress,
        bool completed,
        DateTimeOffset endDate,
        int rewardAmount,
        string rewardType,
        DateTimeOffset fetchedAtUtc,
        string? mapSlug = null,
        string? mapName = null)
    {
        return new DailyMission
        {
            MissionId = missionId,
            Type = type,
            GameMode = gameMode,
            CurrentProgress = currentProgress,
            TargetProgress = targetProgress,
            Completed = completed,
            EndDate = endDate,
            RewardAmount = rewardAmount,
            RewardType = rewardType,
            FetchedAtUtc = fetchedAtUtc,
            MapSlug = mapSlug,
            MapName = mapName
        };
    }

    private DailyMission()
    {
    }
}
