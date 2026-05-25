namespace Entities;

public class ClubMemberExcuse : BaseEntity
{
    public Guid ExcuseId { get; private set; }

    public string UserId { get; private set; } = string.Empty;

    public DateTimeOffset From { get; private set; }

    public DateTimeOffset To { get; private set; }

    public ClubMember? ClubMember { get; private set; }

    public static ClubMemberExcuse Create(string userId, DateTimeOffset from, DateTimeOffset to)
    {
        if (from >= to)
        {
            throw new ArgumentException("Excuse 'from' must be earlier than 'to'.");
        }

        return new ClubMemberExcuse
        {
            ExcuseId = Guid.NewGuid(),
            UserId = userId,
            From = from,
            To = to
        };
    }

    public void UpdateTimeRange(DateTimeOffset from, DateTimeOffset to)
    {
        if (from >= to)
        {
            throw new ArgumentException("Excuse 'from' must be earlier than 'to'.");
        }

        From = from;
        To = to;
    }

    public bool Covers(DateTimeOffset moment) => moment >= From && moment <= To;

    private ClubMemberExcuse()
    {
    }

    public override string ToString()
    {
        return $"{From:d} - {To:d} (Id: {ExcuseId})";
    }

    public string ToStringWithPlayerName()
    {
        return $"Player {ClubMember?.User?.Nickname ?? "N/A"}: {From:d} - {To:d} (Id: {ExcuseId})";
    }
}
