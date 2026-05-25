namespace Entities;

public class ClubMemberHistoryEntry : BaseEntity
{
    public DateTimeOffset Timestamp { get; private set; }

    public string UserId { get; private set; } = string.Empty;

    public Guid ClubId { get; private set; }

    public int Xp { get; private set; }

    public ClubMember? ClubMember { get; private set; }

    public Club? Club { get; private set; }

    public static ClubMemberHistoryEntry Create(string userId, Guid clubId, int xp, DateTimeOffset timestamp)
    {
        return new ClubMemberHistoryEntry
        {
            UserId = userId,
            ClubId = clubId,
            Xp = xp,
            Timestamp = timestamp
        };
    }

    private ClubMemberHistoryEntry()
    {
    }

    public override string ToString() => $"{Timestamp:d}: {Xp}XP";
}
