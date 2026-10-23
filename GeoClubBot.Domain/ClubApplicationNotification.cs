namespace Entities;

/// <summary>
/// Records that a pending application to a club was announced. GeoGuessr gives an application no
/// id, so the applicant identifies it; the record is removed once the application is no longer
/// pending, so a later re-application is announced again.
/// </summary>
public class ClubApplicationNotification : BaseEntity
{
    public int Id { get; private set; }

    public Guid ClubId { get; private set; }

    public string UserId { get; private set; } = string.Empty;

    public DateTimeOffset NotifiedAt { get; private set; }

    public static ClubApplicationNotification Create(Guid clubId, string userId, DateTimeOffset notifiedAt) =>
        new()
        {
            ClubId = clubId,
            UserId = userId,
            NotifiedAt = notifiedAt
        };

    private ClubApplicationNotification()
    {
    }
}
