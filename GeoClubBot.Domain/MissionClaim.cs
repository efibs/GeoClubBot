namespace Entities;

/// <summary>
/// One claim of a board mission, kept after the board week is gone. GeoGuessr only serves the
/// current and the previous board, so this record is the only history of how long members take
/// to finish the missions they claim.
/// </summary>
public class MissionClaim : BaseEntity
{
    public int Id { get; private set; }

    public Guid ClubId { get; private set; }

    /// <summary>Start of the board week; mission ids are only unique within a club's week.</summary>
    public DateTimeOffset PeriodStart { get; private set; }

    public DateTimeOffset PeriodEnd { get; private set; }

    public Guid MissionId { get; private set; }

    /// <summary>GeoGuessr user id of the claimer.</summary>
    public string UserId { get; private set; } = null!;

    public DateTimeOffset ClaimedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Time from claim to completion; null while the mission is not completed.</summary>
    public TimeSpan? CompletionTime => CompletedAt - ClaimedAt;

    /// <summary>The board week ended before the mission was completed.</summary>
    public bool IsAbandoned(DateTimeOffset now) => CompletedAt is null && PeriodEnd <= now;

    /// <summary>Claimed, not completed, and the board week is still running.</summary>
    public bool IsOpen(DateTimeOffset now) => CompletedAt is null && PeriodEnd > now;

    public static MissionClaim Create(
        Guid clubId,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        Guid missionId,
        string userId,
        DateTimeOffset claimedAt,
        DateTimeOffset? completedAt) =>
        new()
        {
            ClubId = clubId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            MissionId = missionId,
            UserId = userId,
            ClaimedAt = claimedAt,
            CompletedAt = completedAt
        };

    /// <summary>Takes over what the board shows now; returns whether anything changed.</summary>
    public bool Update(string userId, DateTimeOffset claimedAt, DateTimeOffset? completedAt)
    {
        if (UserId == userId && ClaimedAt == claimedAt && CompletedAt == completedAt)
        {
            return false;
        }

        UserId = userId;
        ClaimedAt = claimedAt;
        CompletedAt = completedAt;
        return true;
    }

    private MissionClaim()
    {
    }
}
