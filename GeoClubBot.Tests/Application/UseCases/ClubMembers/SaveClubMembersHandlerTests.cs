using Entities;
using Entities.Events;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using NSubstitute;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.ClubMembers;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.ClubMembers;

public sealed class SaveClubMembersHandlerTests
{
    private const string UserId = "user-1";

    private static readonly Guid ClubId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Joined = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly IGeoGuessrUserRepository _users = Substitute.For<IGeoGuessrUserRepository>();
    private readonly IClubMemberRepository _members = Substitute.For<IClubMemberRepository>();

    private readonly List<GeoGuessrUser> _addedUsers = [];
    private readonly List<ClubMember> _addedMembers = [];

    public SaveClubMembersHandlerTests()
    {
        _users.ReadForUpdateByUserIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((GeoGuessrUser?)null);
        _members.ReadForUpdateByUserIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((ClubMember?)null);
        _users.When(u => u.AddUser(Arg.Any<GeoGuessrUser>())).Do(call => _addedUsers.Add(call.Arg<GeoGuessrUser>()));
        _members.When(m => m.AddClubMember(Arg.Any<ClubMember>())).Do(call => _addedMembers.Add(call.Arg<ClubMember>()));
    }

    private Task HandleAsync(params ClubMemberSyncSnapshot[] snapshots) =>
        new SaveClubMembersHandler(_users, _members).Handle(new SaveClubMembersCommand(snapshots), CancellationToken.None);

    private static ClubMemberSyncSnapshot Snapshot(string userId = UserId, string nickname = "New", Guid? clubId = null) =>
        new(userId, nickname, clubId ?? ClubId, 70, Joined);

    [Fact]
    public async Task ANewPlayer_GetsAUserAndAMember()
    {
        await HandleAsync(Snapshot());

        _addedUsers.Should().ContainSingle().Which.Nickname.Should().Be("New");
        var member = _addedMembers.Should().ContainSingle().Subject;
        member.UserId.Should().Be(UserId);
        member.ClubId.Should().Be(ClubId);
        member.Xp.Should().Be(70);
        member.JoinedAt.Should().Be(Joined);
        member.User.Should().BeSameAs(_addedUsers.Single());
    }

    [Fact]
    public async Task AKnownUser_IsRenamed_NotAddedAgain()
    {
        var user = GeoGuessrUser.Create(UserId, "Old");
        _users.ReadForUpdateByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);

        await HandleAsync(Snapshot(nickname: "New"));

        user.Nickname.Should().Be("New");
        _addedUsers.Should().BeEmpty();
    }

    [Fact]
    public async Task ANewMemberOfAKnownUser_IsLinkedToThatUser()
    {
        var user = GeoGuessrUser.Create(UserId, "Old");
        _users.ReadForUpdateByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);

        await HandleAsync(Snapshot());

        _addedMembers.Should().ContainSingle().Which.User.Should().BeSameAs(user);
    }

    [Fact]
    public async Task AKnownMember_IsSynced_NotAddedAgain()
    {
        var member = new ClubMemberBuilder().WithUserId(UserId).WithXp(10).InClub(null).Build();
        _members.ReadForUpdateByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(member);

        await HandleAsync(Snapshot());

        member.Xp.Should().Be(70);
        member.ClubId.Should().Be(ClubId);
        member.JoinedAt.Should().Be(Joined);
        member.DomainEvents.Should().ContainSingle(e => e is PlayerJoinedClubEvent);
        _addedMembers.Should().BeEmpty();
    }

    [Fact]
    public async Task EverySnapshot_IsSaved()
    {
        await HandleAsync(Snapshot("a"), Snapshot("b"));

        _addedMembers.Select(m => m.UserId).Should().Equal("a", "b");
    }
}
