namespace Entities;

public class ClubMemberStrike : BaseEntity
{
    public Guid StrikeId { get; private set; }

    public string UserId { get; private set; } = string.Empty;

    public DateTimeOffset Timestamp { get; private set; }

    public bool Revoked { get; private set; }

    public ClubMember? ClubMember { get; private set; }

    public bool IsActive => !Revoked;

    public static ClubMemberStrike Create(string userId, DateTimeOffset timestamp)
    {
        return new ClubMemberStrike
        {
            StrikeId = Guid.NewGuid(),
            UserId = userId,
            Timestamp = timestamp,
            Revoked = false
        };
    }

    public void Revoke()
    {
        Revoked = true;
    }

    public void Unrevoke()
    {
        Revoked = false;
    }

    public bool IsExpired(DateTimeOffset asOf, TimeSpan decay) => Timestamp + decay < asOf;

    private ClubMemberStrike()
    {
    }

    public override string ToString()
    {
        return $"{Timestamp:d} - Revoked: {Revoked} (Id: {StrikeId})";
    }

    public string ToStringDetailed(TimeSpan expirationTimeSpan)
    {
        var expiration = Timestamp + expirationTimeSpan;
        return $"Player {ClubMember?.User?.Nickname ?? "N/A"}: {Timestamp:d} - Revoked: {Revoked} (Id: {StrikeId}, expires: {expiration:d})";
    }
}
