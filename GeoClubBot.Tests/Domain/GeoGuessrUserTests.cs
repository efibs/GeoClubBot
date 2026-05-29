using Entities;
using Entities.Events;
using FluentAssertions;
using Xunit;

namespace GeoClubBot.Tests.Domain;

public sealed class GeoGuessrUserTests
{
    [Fact]
    public void Create_SetsFields_AndRaisesUserCreatedEvent()
    {
        var user = GeoGuessrUser.Create("user-1", "Alice", 42UL);

        user.UserId.Should().Be("user-1");
        user.Nickname.Should().Be("Alice");
        user.DiscordUserId.Should().Be(42UL);
        user.DomainEvents.OfType<UserCreatedEvent>().Should().ContainSingle()
            .Which.Should().Be(new UserCreatedEvent("user-1", "Alice"));
    }

    [Fact]
    public void Create_WithoutDiscordId_LeavesDiscordIdNull()
    {
        var user = GeoGuessrUser.Create("user-1", "Alice");

        user.DiscordUserId.Should().BeNull();
    }

    [Fact]
    public void UpdateFromApi_WhenNicknameChanged_UpdatesAndRaisesEvent()
    {
        var user = GeoGuessrUser.Create("user-1", "Alice");
        user.ClearDomainEvents();

        var changed = user.UpdateFromApi("Bob");

        changed.Should().BeTrue();
        user.Nickname.Should().Be("Bob");
        user.DomainEvents.OfType<UserUpdatedEvent>().Should().ContainSingle();
    }

    [Fact]
    public void UpdateFromApi_WhenNicknameUnchanged_NoOpAndNoEvent()
    {
        var user = GeoGuessrUser.Create("user-1", "Alice");
        user.ClearDomainEvents();

        var changed = user.UpdateFromApi("Alice");

        changed.Should().BeFalse();
        user.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void LinkDiscord_SetsId_AndRaisesAccountLinkedEvent()
    {
        var user = GeoGuessrUser.Create("user-1", "Alice");
        user.ClearDomainEvents();

        user.LinkDiscord(99UL);

        user.DiscordUserId.Should().Be(99UL);
        user.DomainEvents.OfType<AccountLinkedEvent>().Should().ContainSingle()
            .Which.Should().Be(new AccountLinkedEvent("user-1", "Alice", 99UL));
    }

    [Fact]
    public void UnlinkDiscord_WhenLinked_ClearsId_AndRaisesAccountUnlinkedEvent()
    {
        var user = GeoGuessrUser.Create("user-1", "Alice", 99UL);
        user.ClearDomainEvents();

        user.UnlinkDiscord();

        user.DiscordUserId.Should().BeNull();
        user.DomainEvents.OfType<AccountUnlinkedEvent>().Should().ContainSingle()
            .Which.Should().Be(new AccountUnlinkedEvent("user-1", "Alice", 99UL));
    }

    [Fact]
    public void UnlinkDiscord_WhenNotLinked_NoOpAndNoEvent()
    {
        var user = GeoGuessrUser.Create("user-1", "Alice");
        user.ClearDomainEvents();

        user.UnlinkDiscord();

        user.DiscordUserId.Should().BeNull();
        user.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void ToString_ReturnsNickname()
    {
        GeoGuessrUser.Create("user-1", "Alice").ToString().Should().Be("Alice");
    }
}
