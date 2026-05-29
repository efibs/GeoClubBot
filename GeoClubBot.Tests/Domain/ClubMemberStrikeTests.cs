using Entities;
using FluentAssertions;
using Xunit;

namespace GeoClubBot.Tests.Domain;

public sealed class ClubMemberStrikeTests
{
    [Fact]
    public void Create_IsActive_WithGeneratedId()
    {
        var ts = DateTimeOffset.UtcNow;

        var strike = ClubMemberStrike.Create("user-1", ts);

        strike.UserId.Should().Be("user-1");
        strike.Timestamp.Should().Be(ts);
        strike.Revoked.Should().BeFalse();
        strike.IsActive.Should().BeTrue();
        strike.StrikeId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Create_GeneratesUniqueIds()
    {
        var a = ClubMemberStrike.Create("u", DateTimeOffset.UtcNow);
        var b = ClubMemberStrike.Create("u", DateTimeOffset.UtcNow);

        a.StrikeId.Should().NotBe(b.StrikeId);
    }

    [Fact]
    public void Revoke_MarksRevokedAndInactive()
    {
        var strike = ClubMemberStrike.Create("u", DateTimeOffset.UtcNow);

        strike.Revoke();

        strike.Revoked.Should().BeTrue();
        strike.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Unrevoke_RestoresActive()
    {
        var strike = ClubMemberStrike.Create("u", DateTimeOffset.UtcNow);
        strike.Revoke();

        strike.Unrevoke();

        strike.Revoked.Should().BeFalse();
        strike.IsActive.Should().BeTrue();
    }

    [Fact]
    public void IsExpired_TrueWhenOlderThanDecay()
    {
        var now = DateTimeOffset.UtcNow;
        var strike = ClubMemberStrike.Create("u", now.AddDays(-31));

        strike.IsExpired(now, TimeSpan.FromDays(30)).Should().BeTrue();
    }

    [Fact]
    public void IsExpired_FalseWhenWithinDecay()
    {
        var now = DateTimeOffset.UtcNow;
        var strike = ClubMemberStrike.Create("u", now.AddDays(-10));

        strike.IsExpired(now, TimeSpan.FromDays(30)).Should().BeFalse();
    }

    [Fact]
    public void IsExpired_FalseExactlyAtDecayBoundary()
    {
        var now = DateTimeOffset.UtcNow;
        // Timestamp + decay == asOf -> strictly-less-than comparison means not yet expired.
        var strike = ClubMemberStrike.Create("u", now - TimeSpan.FromDays(30));

        strike.IsExpired(now, TimeSpan.FromDays(30)).Should().BeFalse();
    }
}
