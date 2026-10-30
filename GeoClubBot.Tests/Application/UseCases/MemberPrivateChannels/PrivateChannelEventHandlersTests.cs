using Entities;
using Entities.Events;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.MemberPrivateChannels;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.MemberPrivateChannels;

/// <summary>Domain events run on the back of a save, so none of these handlers may ever throw.</summary>
public sealed class PrivateChannelEventHandlersTests
{
    private const ulong ChannelId = 500;
    private const ulong DiscordUserId = 42;
    private const string UserId = "user-1";
    private const string Nickname = "Player1";

    private static readonly Guid ClubId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Error Refused = Error.Unexpected("member_private_channel.failed", "Discord said no");

    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IClubMemberRepository _members = Substitute.For<IClubMemberRepository>();
    private readonly IDiscordTextChannelAccess _channels = Substitute.For<IDiscordTextChannelAccess>();

    private readonly PlayerJoinedClubEvent _joined = new(UserId, Nickname, DiscordUserId, ClubId, null);
    private readonly PlayerLeftClubEvent _left = new(UserId, Nickname, DiscordUserId, ClubId, ChannelId);
    private readonly AccountLinkedEvent _linked = new(UserId, Nickname, DiscordUserId);
    private readonly AccountUnlinkedEvent _unlinked = new(UserId, Nickname, DiscordUserId);

    public PrivateChannelEventHandlersTests()
    {
        _mediator.Send(Arg.Any<ArchiveMemberPrivateChannelCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _mediator.Send(Arg.Any<RestoreMemberPrivateChannelCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _mediator.Send(Arg.Any<DeleteMemberPrivateChannelCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
    }

    private ClubMember StoredMember(ulong? channelId = null, DateTimeOffset? archivedAt = null, Guid? clubId = default)
    {
        var builder = new ClubMemberBuilder().WithUserId(UserId).WithNickname(Nickname).WithDiscordUserId(DiscordUserId).InClub(clubId ?? ClubId);
        if (channelId is { } id)
        {
            builder.WithPrivateChannel(id, archivedAt);
        }

        var member = builder.Build();
        _members.ReadClubMemberByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(member);
        return member;
    }

    private void NoStoredMember() =>
        _members.ReadClubMemberByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((ClubMember?)null);

    private void TheRepositoryIsDown() =>
        _members.ReadClubMemberByUserIdAsync(UserId, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("db down"));

    // ---- Joined -----------------------------------------------------------

    private Task JoinAsync(PlayerJoinedClubEvent notification) =>
        new HandlePlayerJoinedClubForPrivateChannelUseCase(_mediator, _members, NullLogger<HandlePlayerJoinedClubForPrivateChannelUseCase>.Instance)
            .Handle(notification, CancellationToken.None);

    [Fact]
    public async Task Join_OfAnUnlinkedPlayer_DoesNothing()
    {
        await JoinAsync(_joined with { DiscordUserId = null });

        _members.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Join_OfAnUnknownMember_DoesNothing()
    {
        NoStoredMember();

        await JoinAsync(_joined);

        _mediator.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Join_WithoutAChannel_CreatesOne()
    {
        var member = StoredMember();

        await JoinAsync(_joined);

        await _mediator.Received(1).Send(new CreateMemberPrivateChannelCommand(member), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Join_WithALiveChannel_LeavesItAlone()
    {
        StoredMember(ChannelId);

        await JoinAsync(_joined);

        _mediator.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Join_WithAnArchivedChannel_RestoresIt_InsteadOfMakingANewOne()
    {
        var member = StoredMember(ChannelId, DateTimeOffset.UtcNow);

        await JoinAsync(_joined);

        await _mediator.Received(1).Send(new RestoreMemberPrivateChannelCommand(member), Arg.Any<CancellationToken>());
        await _mediator.DidNotReceive().Send(Arg.Any<CreateMemberPrivateChannelCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Join_NeverThrows()
    {
        TheRepositoryIsDown();

        await JoinAsync(_joined);
    }

    [Fact]
    public async Task Join_CopesWithARefusedRestore()
    {
        StoredMember(ChannelId, DateTimeOffset.UtcNow);
        _mediator.Send(Arg.Any<RestoreMemberPrivateChannelCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Failure(Refused));

        await JoinAsync(_joined);
    }

    // ---- Left -------------------------------------------------------------

    private Task LeaveAsync(PlayerLeftClubEvent notification) =>
        new HandlePlayerLeftClubForPrivateChannelUseCase(_mediator, _members, NullLogger<HandlePlayerLeftClubForPrivateChannelUseCase>.Instance)
            .Handle(notification, CancellationToken.None);

    [Fact]
    public async Task Leave_OfAnUnlinkedPlayer_DoesNothing()
    {
        await LeaveAsync(_left with { DiscordUserId = null });

        _members.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Leave_WithoutAChannel_DoesNothing()
    {
        StoredMember();

        await LeaveAsync(_left);

        _mediator.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Leave_ArchivesTheChannel()
    {
        var member = StoredMember(ChannelId);

        await LeaveAsync(_left);

        await _mediator.Received(1).Send(new ArchiveMemberPrivateChannelCommand(member), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Leave_NeverThrows()
    {
        TheRepositoryIsDown();

        await LeaveAsync(_left);
    }

    [Fact]
    public async Task Leave_CopesWithARefusedArchive()
    {
        StoredMember(ChannelId);
        _mediator.Send(Arg.Any<ArchiveMemberPrivateChannelCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Failure(Refused));

        await LeaveAsync(_left);
    }

    // ---- Linked -----------------------------------------------------------

    private Task LinkAsync() =>
        new HandleAccountLinkedForPrivateChannelUseCase(_mediator, _members, NullLogger<HandleAccountLinkedForPrivateChannelUseCase>.Instance)
            .Handle(_linked, CancellationToken.None);

    [Fact]
    public async Task Link_OfSomeoneInNoClub_DoesNothing()
    {
        NoStoredMember();

        await LinkAsync();

        _mediator.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Link_OfAFormerMember_DoesNothing()
    {
        var member = new ClubMemberBuilder().WithUserId(UserId).InClub(null).Build();
        _members.ReadClubMemberByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(member);

        await LinkAsync();

        _mediator.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Link_WithoutAChannel_CreatesOne()
    {
        var member = StoredMember();

        await LinkAsync();

        await _mediator.Received(1).Send(new CreateMemberPrivateChannelCommand(member), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Link_WithALiveChannel_LeavesItAlone()
    {
        StoredMember(ChannelId);

        await LinkAsync();

        _mediator.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Link_WithAnArchivedChannel_RestoresIt()
    {
        var member = StoredMember(ChannelId, DateTimeOffset.UtcNow);

        await LinkAsync();

        await _mediator.Received(1).Send(new RestoreMemberPrivateChannelCommand(member), Arg.Any<CancellationToken>());
        await _mediator.DidNotReceive().Send(Arg.Any<CreateMemberPrivateChannelCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Link_NeverThrows()
    {
        TheRepositoryIsDown();

        await LinkAsync();
    }

    [Fact]
    public async Task Link_CopesWithARefusedRestore()
    {
        StoredMember(ChannelId, DateTimeOffset.UtcNow);
        _mediator.Send(Arg.Any<RestoreMemberPrivateChannelCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Failure(Refused));

        await LinkAsync();
    }

    // ---- Unlinked ---------------------------------------------------------

    private Task UnlinkAsync() =>
        new HandleAccountUnlinkedForPrivateChannelUseCase(_mediator, _members, NullLogger<HandleAccountUnlinkedForPrivateChannelUseCase>.Instance)
            .Handle(_unlinked, CancellationToken.None);

    [Fact]
    public async Task Unlink_DeletesTheChannel()
    {
        var member = StoredMember(ChannelId);

        await UnlinkAsync();

        await _mediator.Received(1).Send(new DeleteMemberPrivateChannelCommand(member), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unlink_NeverThrows()
    {
        TheRepositoryIsDown();

        await UnlinkAsync();
    }

    [Fact]
    public async Task Unlink_CopesWithARefusedDeletion()
    {
        StoredMember(ChannelId);
        _mediator.Send(Arg.Any<DeleteMemberPrivateChannelCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Failure(Refused));

        await UnlinkAsync();
    }

    // ---- Nickname changed -------------------------------------------------

    private readonly UserUpdatedEvent _renamed = new(UserId, Nickname, "NewName", DiscordUserId, DiscordUserId);

    private Task RenameAsync(UserUpdatedEvent notification) =>
        new HandleUserNicknameChangedForPrivateChannelUseCase(_members, _channels, NullLogger<HandleUserNicknameChangedForPrivateChannelUseCase>.Instance)
            .Handle(notification, CancellationToken.None);

    [Fact]
    public async Task Rename_FollowsTheMembersStoredNickname()
    {
        var member = new ClubMemberBuilder().WithUserId(UserId).WithNickname("NewName").WithPrivateChannel(ChannelId).Build();
        _members.ReadClubMemberByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(member);

        await RenameAsync(_renamed);

        await _channels.Received(1).UpdateTextChannelAsync(
            new TextChannel(ChannelId) { Name = "newname-private-channel" },
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rename_IsSkipped_WhenOnlyTheDiscordLinkChanged()
    {
        await RenameAsync(_renamed with { NewDiscordUserId = 99 });

        _members.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Rename_IsSkipped_WhenTheNicknameIsTheSame()
    {
        await RenameAsync(_renamed with { NewNickname = Nickname });

        _members.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Rename_IsSkipped_ForSomeoneInNoClub()
    {
        var member = new ClubMemberBuilder().WithUserId(UserId).InClub(null).WithPrivateChannel(ChannelId).Build();
        _members.ReadClubMemberByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(member);

        await RenameAsync(_renamed);

        _channels.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Rename_IsSkipped_WithoutAChannel()
    {
        StoredMember();

        await RenameAsync(_renamed);

        _channels.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Rename_NeverThrows()
    {
        TheRepositoryIsDown();

        await RenameAsync(_renamed);
    }
}
