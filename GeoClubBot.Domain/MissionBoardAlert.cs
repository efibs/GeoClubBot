namespace Entities;

public enum MissionBoardAlertKind
{
    /// <summary>A mission was claimed a long time ago and is still not completed.</summary>
    OpenClaim = 0,

    /// <summary>The claimer requested help a while ago and the mission is still not completed.</summary>
    HelpRequest = 1
}

/// <summary>
/// Records that an alert about a stuck board mission was sent, so each mission is alerted about at
/// most once per kind — across restarts and however often the check runs.
/// </summary>
public class MissionBoardAlert : BaseEntity
{
    public int Id { get; private set; }

    public Guid ClubId { get; private set; }

    public Guid MissionId { get; private set; }

    public MissionBoardAlertKind Kind { get; private set; }

    public DateTimeOffset SentAt { get; private set; }

    public static MissionBoardAlert Create(Guid clubId, Guid missionId, MissionBoardAlertKind kind, DateTimeOffset sentAt) =>
        new()
        {
            ClubId = clubId,
            MissionId = missionId,
            Kind = kind,
            SentAt = sentAt
        };

    private MissionBoardAlert()
    {
    }
}
