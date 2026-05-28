using Entities;

namespace GeoClubBot.Tests.TestBuilders;

public sealed class HistoryEntryBuilder
{
    private string _userId = "user-1";
    private Guid _clubId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private int _xp = 1000;
    private DateTimeOffset _timestamp = DateTimeOffset.UtcNow;

    public HistoryEntryBuilder WithUserId(string userId)
    {
        _userId = userId;
        return this;
    }

    public HistoryEntryBuilder InClub(Guid clubId)
    {
        _clubId = clubId;
        return this;
    }

    public HistoryEntryBuilder WithXp(int xp)
    {
        _xp = xp;
        return this;
    }

    public HistoryEntryBuilder At(DateTimeOffset timestamp)
    {
        _timestamp = timestamp;
        return this;
    }

    public ClubMemberHistoryEntry Build() =>
        ClubMemberHistoryEntry.Create(_userId, _clubId, _xp, _timestamp);
}
