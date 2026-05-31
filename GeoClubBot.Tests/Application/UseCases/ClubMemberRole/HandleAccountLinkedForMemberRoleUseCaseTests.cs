using Entities.Events;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using Microsoft.Extensions.Logging;
using NSubstitute;
using UseCases.OutputPorts.Repositories;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.ClubMemberRole;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.ClubMemberRole;

public sealed class HandleAccountLinkedForMemberRoleUseCaseTests
{
    private const ulong DiscordUserId = 555UL;
    private const ulong ClubRoleId = 4242UL;
    private static readonly Guid ClubId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly IClubMemberRepository _members = Substitute.For<IClubMemberRepository>();
    private readonly IDiscordServerRolesAccess _roles = Substitute.For<IDiscordServerRolesAccess>();

    private HandleAccountLinkedForMemberRoleUseCase CreateHandler(ulong? roleId = ClubRoleId)
    {
        var config = new GeoGuessrConfigurationBuilder()
            .WithClub(ClubId, roleId: roleId)
            .BuildOptions();
        return new HandleAccountLinkedForMemberRoleUseCase(
            _members, _roles, config,
            Substitute.For<ILogger<HandleAccountLinkedForMemberRoleUseCase>>());
    }

    private static AccountLinkedEvent Event() => new("user-1", "Player1", DiscordUserId);

    [Fact]
    public async Task Handle_AddsClubRole_WhenMemberIsInClubWithRole()
    {
        var member = new ClubMemberBuilder().WithUserId("user-1").InClub(ClubId).Build();
        _members.ReadClubMemberByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(member);

        await CreateHandler().Handle(Event(), CancellationToken.None);

        await _roles.Received(1).AddRoleToMembersByUserIdsAsync(
            Arg.Is<IEnumerable<ulong>>(ids => ids.Single() == DiscordUserId),
            ClubRoleId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DoesNothing_WhenMemberNotFound()
    {
        _members.ReadClubMemberByUserIdAsync("user-1", Arg.Any<CancellationToken>())
            .Returns((global::Entities.ClubMember?)null);

        await CreateHandler().Handle(Event(), CancellationToken.None);

        await _roles.DidNotReceive().AddRoleToMembersByUserIdsAsync(
            Arg.Any<IEnumerable<ulong>>(), Arg.Any<ulong>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DoesNothing_WhenMemberHasNoClub()
    {
        var member = new ClubMemberBuilder().WithUserId("user-1").InClub(null).Build();
        _members.ReadClubMemberByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(member);

        await CreateHandler().Handle(Event(), CancellationToken.None);

        await _roles.DidNotReceive().AddRoleToMembersByUserIdsAsync(
            Arg.Any<IEnumerable<ulong>>(), Arg.Any<ulong>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DoesNothing_WhenClubHasNoConfiguredRole()
    {
        var member = new ClubMemberBuilder().WithUserId("user-1").InClub(ClubId).Build();
        _members.ReadClubMemberByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(member);

        await CreateHandler(roleId: null).Handle(Event(), CancellationToken.None);

        await _roles.DidNotReceive().AddRoleToMembersByUserIdsAsync(
            Arg.Any<IEnumerable<ulong>>(), Arg.Any<ulong>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SwallowsExceptions_FromRolesAccess()
    {
        var member = new ClubMemberBuilder().WithUserId("user-1").InClub(ClubId).Build();
        _members.ReadClubMemberByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(member);
        _roles.AddRoleToMembersByUserIdsAsync(Arg.Any<IEnumerable<ulong>>(), Arg.Any<ulong>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("discord down")));

        var act = async () => await CreateHandler().Handle(Event(), CancellationToken.None);

        await act.Should().NotThrowAsync("the handler logs and swallows errors to avoid breaking event dispatch");
    }
}
