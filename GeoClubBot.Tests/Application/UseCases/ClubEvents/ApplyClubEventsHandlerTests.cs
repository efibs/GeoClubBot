using Configuration;
using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using GeoClubBot.Tests.TestDoubles;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.Club;
using UseCases.UseCases.ClubEvents;
using UseCases.UseCases.ClubMembers;
using UseCases.UseCases.MissionBoard;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.ClubEvents;

public sealed class ApplyClubEventsHandlerTests
{
    private static readonly Guid ClubA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClubB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset JoinedAt = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly IClubMemberRepository _members = Substitute.For<IClubMemberRepository>();
    private readonly IGeoGuessrCacheInvalidator _cache = Substitute.For<IGeoGuessrCacheInvalidator>();
    private readonly RecordingClubMemberSyncGate _gate = new();
    private readonly List<object> _sent = [];
    private readonly List<bool> _gateHeldWhileSending = [];
    private IReadOnlyList<ClubMemberSyncSnapshot>? _saved;
    private long _sequence;

    public ApplyClubEventsHandlerTests()
    {
        _members.ReadClubMembersByUserIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, ClubMember>());
        _sender.Send(Arg.Any<IRequest<Unit>>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var request = call.Arg<IRequest<Unit>>();
            _sent.Add(request);
            _gateHeldWhileSending.Add(_gate.IsHeld);
            if (request is SaveClubMembersCommand save)
            {
                _saved = save.Snapshots;
            }

            return Unit.Value;
        });
    }

    private Task<bool> HandleAsync(params ClubPushEvent[] events) => HandleAsync(new GeoGuessrSocketConfiguration(), events);

    private Task<bool> HandleAsync(GeoGuessrSocketConfiguration socket, params ClubPushEvent[] events)
    {
        var scopes = TestScopes.Serving(services => services.AddSingleton(_sender).AddSingleton(_members));
        var config = new GeoGuessrConfigurationBuilder().WithClub(ClubA).WithClub(ClubB, isMain: false);
        return new ApplyClubEventsHandler(
                scopes, _gate, _cache, Options.Create(socket), config.BuildOptions(),
                NullLogger<ApplyClubEventsHandler>.Instance)
            .Handle(new ApplyClubEventsCommand(events), CancellationToken.None);
    }

    private PushOrder Next() => new(null, ++_sequence);

    private ClubMemberJoinedPush Joined(Guid club, string user) => new(club, Next(), user, $"nick-{user}", 25, JoinedAt);

    private ClubMemberLeftPush Left(Guid club, string user) => new(club, Next(), user);

    private void Stored(string userId, Guid? club, int xp = 300)
    {
        var builder = new ClubMemberBuilder().WithUserId(userId).WithNickname($"stored-{userId}").WithXp(xp).JoinedAt(JoinedAt.AddDays(-50));
        var member = builder.InClub(club).Build();
        _members.ReadClubMembersByUserIdsAsync(
                Arg.Is<IReadOnlyCollection<string>>(ids => ids.Contains(userId)), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, ClubMember> { [userId] = member });
    }

    [Fact]
    public async Task AJoin_IsSavedFromThePushedData_InsideTheMemberGate()
    {
        var complete = await HandleAsync(Joined(ClubA, "u1"));

        complete.Should().BeTrue();
        _saved.Should().Equal(new ClubMemberSyncSnapshot("u1", "nick-u1", ClubA, 25, JoinedAt));
        _gateHeldWhileSending.Should().Equal(true);
        _sent.Should().NotContain(r => r is SyncClubsCommand);
    }

    [Fact]
    public async Task ALeave_KeepsTheStoredDetails_AndClearsTheClub()
    {
        Stored("u1", ClubA);

        await HandleAsync(Left(ClubA, "u1"));

        _saved.Should().Equal(new ClubMemberSyncSnapshot("u1", "stored-u1", null, 300, JoinedAt.AddDays(-50)));
    }

    [Fact]
    public async Task ALeaveOfAClubTheMemberIsNoLongerStoredIn_SavesNothing()
    {
        Stored("u1", ClubB);

        await HandleAsync(Left(ClubA, "u1"));

        _sent.Should().NotContain(r => r is SaveClubMembersCommand);
    }

    [Fact]
    public async Task ALeaveOfAnUnknownMember_SavesNothing()
    {
        await HandleAsync(Left(ClubA, "stranger"));

        _sent.Should().NotContain(r => r is SaveClubMembersCommand);
    }

    [Fact]
    public async Task ASwitchAcrossTwoSockets_IsSavedAsOneMoveToTheNewClub()
    {
        Stored("u1", ClubA);

        await HandleAsync(Joined(ClubB, "u1"), Left(ClubA, "u1"));

        _saved.Should().Equal(new ClubMemberSyncSnapshot("u1", "nick-u1", ClubB, 25, JoinedAt));
    }

    [Fact]
    public async Task ARosterResync_RunsTheFullSync_OutsideTheMemberGate()
    {
        await HandleAsync(Joined(ClubA, "u1"), new ClubRosterChangedPush(ClubA, Next()));

        _sent.Should().ContainSingle(r => r is SyncClubsCommand);
        _sent.Should().NotContain(r => r is SaveClubMembersCommand);
        // The sync takes the gate itself; holding it here would deadlock.
        _gateHeldWhileSending.Should().AllBeEquivalentTo(false);
    }

    [Fact]
    public async Task ARefresh_ReReadsTheClubsLevelAndRecordsItsBoard()
    {
        await HandleAsync(new ClubRefreshRequested(ClubB, Next(), ClubRefreshReason.Connected));

        _sent.Should().ContainEquivalentOf(new CheckClubLevelCommand(ClubB));
        _sent.Should().ContainEquivalentOf(new RecordMissionClaimsCommand(ClubB));
        _sent.Should().ContainSingle(r => r is SyncClubsCommand);
        _cache.Received(1).InvalidateCurrentMissionBoard(ClubB);
    }

    [Fact]
    public async Task ABoardUpdate_DropsTheCachedBoard_BeforeRecordingTheClaims()
    {
        _sender.Send(Arg.Any<RecordMissionClaimsCommand>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _cache.Received(1).InvalidateCurrentMissionBoard(ClubA);
            return Unit.Value;
        });

        await HandleAsync(new ClubMissionBoardUpdatedPush(ClubA, Next()));

        await _sender.Received(1).Send(new RecordMissionClaimsCommand(ClubA), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AClubUpdate_AppliesThePushedLevelAndName_WithoutReadingTheClub()
    {
        await HandleAsync(new ClubUpdatedPush(ClubA, Next(), 12, "Renamed"));

        _sent.Should().ContainEquivalentOf(new ApplyClubLevelCommand(ClubA, 12));
        _sent.Should().ContainEquivalentOf(new RenameClubCommand(ClubA, "Renamed"));
        _sent.Should().NotContain(r => r is CheckClubLevelCommand);
    }

    [Fact]
    public async Task TheWebsitesCachedClub_IsDropped_ForEveryClubThatChanged()
    {
        Stored("u2", ClubB);

        await HandleAsync(Joined(ClubA, "u1"), Left(ClubB, "u2"));

        _cache.Received(1).InvalidateClub(ClubA);
        _cache.Received(1).InvalidateClub(ClubB);
    }

    [Fact]
    public async Task AResync_DropsEveryClubsCachedFigures()
    {
        await HandleAsync(new ClubRosterChangedPush(ClubA, Next()));

        _cache.Received(1).InvalidateClub(ClubA);
        _cache.Received(1).InvalidateClub(ClubB);
    }

    [Fact]
    public async Task AFailingStep_DoesNotStopTheOthers_AndReportsTheBatchIncomplete()
    {
        _sender.Send(Arg.Any<ApplyClubLevelCommand>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("boom"));

        var complete = await HandleAsync(new ClubUpdatedPush(ClubA, Next(), 12, null), Joined(ClubA, "u1"), new ClubMissionBoardUpdatedPush(ClubA, Next()));

        complete.Should().BeFalse();
        _saved.Should().NotBeNull();
        await _sender.Received(1).Send(new RecordMissionClaimsCommand(ClubA), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FailingMemberChanges_ReleaseTheGate_AndReportTheBatchIncomplete()
    {
        _members.ReadClubMembersByUserIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("db down"));

        var complete = await HandleAsync(Left(ClubA, "u1"));

        complete.Should().BeFalse();
        _gate.IsHeld.Should().BeFalse();
    }

    [Fact]
    public async Task ACancellation_IsNotSwallowed()
    {
        _sender.Send(Arg.Any<RecordMissionClaimsCommand>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        var act = () => HandleAsync(new ClubMissionBoardUpdatedPush(ClubA, Next()));

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task NothingToDo_SendsNothing()
    {
        var complete = await HandleAsync(new GeoGuessrSocketConfiguration { MissionBoardEvents = false }, new ClubMissionBoardUpdatedPush(ClubA, Next()));

        complete.Should().BeTrue();
        _sent.Should().BeEmpty();
        _cache.ReceivedCalls().Should().BeEmpty();
    }
}
