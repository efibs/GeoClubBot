using Entities;

namespace GeoClubBot.Tests.TestBuilders;

public sealed class ClubMemberBuilder
{
    private string _userId = "user-1";
    private string _nickname = "Player1";
    private ulong? _discordUserId;
    private Guid? _clubId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private int _xp = 1000;
    private DateTimeOffset _joinedAt = DateTimeOffset.UtcNow.AddMonths(-6);
    private readonly List<ClubMemberStrike> _strikes = [];

    public ClubMemberBuilder WithUserId(string userId)
    {
        _userId = userId;
        return this;
    }

    public ClubMemberBuilder WithNickname(string nickname)
    {
        _nickname = nickname;
        return this;
    }

    public ClubMemberBuilder WithDiscordUserId(ulong discordUserId)
    {
        _discordUserId = discordUserId;
        return this;
    }

    public ClubMemberBuilder InClub(Guid? clubId)
    {
        _clubId = clubId;
        return this;
    }

    public ClubMemberBuilder WithXp(int xp)
    {
        _xp = xp;
        return this;
    }

    public ClubMemberBuilder JoinedAt(DateTimeOffset joinedAt)
    {
        _joinedAt = joinedAt;
        return this;
    }

    public ClubMemberBuilder WithStrike(DateTimeOffset timestamp, bool revoked = false)
    {
        var strike = ClubMemberStrike.Create(_userId, timestamp);
        if (revoked) strike.Revoke();
        _strikes.Add(strike);
        return this;
    }

    public ClubMember Build()
    {
        var user = GeoGuessrUser.Create(_userId, _nickname, _discordUserId);
        var member = ClubMember.Create(user, _clubId, _xp, _joinedAt);
        foreach (var strike in _strikes)
        {
            member.Strikes.Add(strike);
        }
        return member;
    }
}
