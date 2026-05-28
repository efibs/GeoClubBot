using Entities;

namespace GeoClubBot.Tests.TestBuilders;

public sealed class ExcuseBuilder
{
    private string _userId = "user-1";
    private DateTimeOffset _from = DateTimeOffset.UtcNow.AddDays(-7);
    private DateTimeOffset _to = DateTimeOffset.UtcNow.AddDays(-1);

    public ExcuseBuilder WithUserId(string userId)
    {
        _userId = userId;
        return this;
    }

    public ExcuseBuilder Covering(DateTimeOffset from, DateTimeOffset to)
    {
        _from = from;
        _to = to;
        return this;
    }

    public ClubMemberExcuse Build() => ClubMemberExcuse.Create(_userId, _from, _to);
}
