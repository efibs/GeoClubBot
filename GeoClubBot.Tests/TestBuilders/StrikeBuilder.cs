using Entities;

namespace GeoClubBot.Tests.TestBuilders;

public sealed class StrikeBuilder
{
    private string _userId = "user-1";
    private DateTimeOffset _timestamp = DateTimeOffset.UtcNow;
    private bool _revoked;

    public StrikeBuilder WithUserId(string userId)
    {
        _userId = userId;
        return this;
    }

    public StrikeBuilder At(DateTimeOffset timestamp)
    {
        _timestamp = timestamp;
        return this;
    }

    public StrikeBuilder Revoked(bool revoked = true)
    {
        _revoked = revoked;
        return this;
    }

    public ClubMemberStrike Build()
    {
        var strike = ClubMemberStrike.Create(_userId, _timestamp);
        if (_revoked) strike.Revoke();
        return strike;
    }
}
