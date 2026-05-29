using Entities;
using Entities.Events;
using FluentAssertions;
using Xunit;

namespace GeoClubBot.Tests.Domain;

public sealed class ClubMemberTests
{
    private static readonly Guid ClubA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClubB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static GeoGuessrUser User() => GeoGuessrUser.Create("user-1", "Alice", 7UL);

    [Fact]
    public void Create_WithClub_RaisesPlayerJoinedClubEvent()
    {
        var member = ClubMember.Create(User(), ClubA, xp: 100, joinedAt: DateTimeOffset.UtcNow);

        member.ClubId.Should().Be(ClubA);
        member.Xp.Should().Be(100);
        member.DomainEvents.OfType<PlayerJoinedClubEvent>().Should().ContainSingle()
            .Which.ClubId.Should().Be(ClubA);
    }

    [Fact]
    public void Create_WithoutClub_DoesNotRaiseJoinedEvent()
    {
        var member = ClubMember.Create(User(), clubId: null, xp: 0, joinedAt: DateTimeOffset.UtcNow);

        member.ClubId.Should().BeNull();
        member.DomainEvents.OfType<PlayerJoinedClubEvent>().Should().BeEmpty();
    }

    [Fact]
    public void SyncFromApi_NullToClub_RaisesJoined()
    {
        var member = ClubMember.Create(User(), clubId: null, xp: 0, joinedAt: DateTimeOffset.UtcNow);
        member.ClearDomainEvents();

        member.SyncFromApi(ClubA, newXp: 50, newJoinedAt: DateTimeOffset.UtcNow);

        member.DomainEvents.OfType<PlayerJoinedClubEvent>().Should().ContainSingle();
        member.Xp.Should().Be(50);
    }

    [Fact]
    public void SyncFromApi_ClubToNull_RaisesLeft()
    {
        var member = ClubMember.Create(User(), ClubA, xp: 100, joinedAt: DateTimeOffset.UtcNow);
        member.ClearDomainEvents();

        member.SyncFromApi(newClubId: null, newXp: 100, newJoinedAt: DateTimeOffset.UtcNow);

        member.DomainEvents.OfType<PlayerLeftClubEvent>().Should().ContainSingle()
            .Which.OldClubId.Should().Be(ClubA);
    }

    [Fact]
    public void SyncFromApi_ClubToDifferentClub_RaisesSwitched()
    {
        var member = ClubMember.Create(User(), ClubA, xp: 100, joinedAt: DateTimeOffset.UtcNow);
        member.ClearDomainEvents();

        member.SyncFromApi(ClubB, newXp: 100, newJoinedAt: DateTimeOffset.UtcNow);

        var switched = member.DomainEvents.OfType<PlayerSwitchedClubsEvent>().Should().ContainSingle().Which;
        switched.OldClubId.Should().Be(ClubA);
        switched.NewClubId.Should().Be(ClubB);
    }

    [Fact]
    public void SyncFromApi_SameClub_RaisesNoMembershipEvent()
    {
        var member = ClubMember.Create(User(), ClubA, xp: 100, joinedAt: DateTimeOffset.UtcNow);
        member.ClearDomainEvents();

        member.SyncFromApi(ClubA, newXp: 250, newJoinedAt: DateTimeOffset.UtcNow);

        member.DomainEvents.Should().BeEmpty();
        member.Xp.Should().Be(250);
    }

    [Fact]
    public void SyncFromApi_NullToNull_RaisesNoEvent()
    {
        var member = ClubMember.Create(User(), clubId: null, xp: 0, joinedAt: DateTimeOffset.UtcNow);
        member.ClearDomainEvents();

        member.SyncFromApi(newClubId: null, newXp: 10, newJoinedAt: DateTimeOffset.UtcNow);

        member.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void SetPrivateTextChannelId_StoresValue()
    {
        var member = ClubMember.Create(User(), ClubA, xp: 0, joinedAt: DateTimeOffset.UtcNow);

        member.SetPrivateTextChannelId(123UL);
        member.PrivateTextChannelId.Should().Be(123UL);

        member.SetPrivateTextChannelId(null);
        member.PrivateTextChannelId.Should().BeNull();
    }

    [Fact]
    public void Left_And_Joined_Events_CarryPrivateChannelId()
    {
        var member = ClubMember.Create(User(), ClubA, xp: 0, joinedAt: DateTimeOffset.UtcNow);
        member.SetPrivateTextChannelId(555UL);
        member.ClearDomainEvents();

        member.SyncFromApi(newClubId: null, newXp: 0, newJoinedAt: DateTimeOffset.UtcNow);

        member.DomainEvents.OfType<PlayerLeftClubEvent>().Single()
            .PrivateTextChannelId.Should().Be(555UL);
    }
}
