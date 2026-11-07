using Configuration;
using FluentAssertions;
using GeoClubBot.MockGeoGuessr.DataStore;
using GeoClubBot.MockGeoGuessr.Endpoints;
using GeoClubBot.MockGeoGuessr.Socket;
using GeoClubBot.Tests.TestBuilders;
using Infrastructure.InputAdapters.GeoGuessrSocket;
using NSubstitute;
using Quartz;
using UseCases.UseCases.ClubEvents;
using Xunit;

namespace GeoClubBot.Tests.Tools;

/// <summary>
/// The mock UI's changes reach the bot over the in-process socket, in the shapes the bot's own decoder
/// reads — so a local run exercises the whole push path.
/// </summary>
public sealed class MockGeoGuessrSocketTests : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly Guid _clubA = Guid.NewGuid();
    private readonly Guid _clubB = Guid.NewGuid();
    private readonly MockGeoGuessrDataStore _store = new();
    private readonly MockClubSocketHub _hub;
    private readonly MockManagementController _controller;
    private readonly List<IGeoGuessrSocketSession> _sessions = [];

    public MockGeoGuessrSocketTests()
    {
        _store.Clubs[_clubA] = GeoGuessrDtos.Club(_clubA, level: 3, name: "A");
        _store.Clubs[_clubB] = GeoGuessrDtos.Club(_clubB, name: "B");
        _hub = new MockClubSocketHub(_store);
        _controller = new MockManagementController(_store, Substitute.For<ISchedulerFactory>(), _hub);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions)
        {
            await session.DisposeAsync();
        }
    }

    private async Task<(IGeoGuessrSocketSession Session, IAsyncEnumerator<string> Frames)> SubscribeAsync(Guid clubId)
    {
        var session = await new MockGeoGuessrSocketConnector(_hub)
            .ConnectAsync(new GeoGuessrClubEntry { ClubId = clubId, NcfaToken = "x" }, false, CancellationToken.None);
        _sessions.Add(session);
        await session.SendAsync(OutboundFrames.Subscribe(SocketTopics.Club(clubId)), CancellationToken.None);
        var frames = session.ReadFramesAsync(CancellationToken.None).GetAsyncEnumerator();
        (await NextAsync(frames, clubId)).Should().BeOfType<DecodedClubFrame.Subscribed>();
        return (session, frames);
    }

    private static async Task<DecodedClubFrame> NextAsync(IAsyncEnumerator<string> frames, Guid clubId)
    {
        (await frames.MoveNextAsync().AsTask().WaitAsync(Timeout)).Should().BeTrue();
        return ClubFrameDecoder.Decode(frames.Current, clubId);
    }

    private string NewUser()
    {
        var userId = Guid.NewGuid().ToString("N")[..24];
        _controller.CreateUser(new CreateUserRequest("Alice", userId));
        return userId;
    }

    [Fact]
    public async Task AddingAMember_PushesAJoinTheBotCanApplyWithoutReadingTheRoster()
    {
        var (_, frames) = await SubscribeAsync(_clubA);
        var userId = NewUser();

        _controller.AddMember(_clubA, new AddMemberRequest(userId));

        var push = (await NextAsync(frames, _clubA)).Should().BeOfType<DecodedClubFrame.Push>().Subject;
        push.PayloadUnusable.Should().BeFalse();
        push.Event.Should().BeOfType<ClubMemberJoinedPush>().Which.Should().Match<ClubMemberJoinedPush>(j => j.UserId == userId && j.Nickname == "Alice");
    }

    [Fact]
    public async Task MovingAMember_PushesALeaveToTheOldClubAndAJoinToTheNewOne()
    {
        var (_, framesA) = await SubscribeAsync(_clubA);
        var (_, framesB) = await SubscribeAsync(_clubB);
        var userId = NewUser();
        _controller.AddMember(_clubA, new AddMemberRequest(userId));
        await NextAsync(framesA, _clubA);

        _controller.MoveMember(_clubA, userId, new MoveMemberRequest(_clubB));

        ((DecodedClubFrame.Push)await NextAsync(framesA, _clubA)).Event.Should().BeOfType<ClubMemberLeftPush>().Which.UserId.Should().Be(userId);
        var joined = ((DecodedClubFrame.Push)await NextAsync(framesB, _clubB)).Event.Should().BeOfType<ClubMemberJoinedPush>().Subject;
        joined.JoinedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1), "a switch is a new membership");
    }

    [Fact]
    public async Task ClubAndBoardChanges_ArePushed()
    {
        var (_, frames) = await SubscribeAsync(_clubA);

        _controller.IncrementLevel(_clubA);
        _controller.UpdateClub(_clubA, new UpdateClubRequest(Name: "Renamed"));
        _controller.ResetBoard(_clubA);

        ((DecodedClubFrame.Push)await NextAsync(frames, _clubA)).Event.Should().BeOfType<ClubUpdatedPush>().Which.Level.Should().Be(4);
        ((DecodedClubFrame.Push)await NextAsync(frames, _clubA)).Event.Should().BeOfType<ClubUpdatedPush>().Which.Name.Should().Be("Renamed");
        ((DecodedClubFrame.Push)await NextAsync(frames, _clubA)).Event.Should().BeOfType<ClubMissionBoardUpdatedPush>();
    }

    [Fact]
    public async Task OnlyTheClubsOwnTopic_GetsItsEvents()
    {
        var (_, framesB) = await SubscribeAsync(_clubB);

        _controller.IncrementLevel(_clubA);
        _controller.IncrementLevel(_clubB);

        ((DecodedClubFrame.Push)await NextAsync(framesB, _clubB)).Event.ClubId.Should().Be(_clubB);
    }

    [Fact]
    public async Task AnUnknownClub_IsDenied()
    {
        var unknown = Guid.NewGuid();
        var session = await new MockGeoGuessrSocketConnector(_hub)
            .ConnectAsync(new GeoGuessrClubEntry { ClubId = unknown, NcfaToken = "x" }, false, CancellationToken.None);
        _sessions.Add(session);

        await session.SendAsync(OutboundFrames.Subscribe(SocketTopics.Club(unknown)), CancellationToken.None);

        (await NextAsync(session.ReadFramesAsync(CancellationToken.None).GetAsyncEnumerator(), unknown))
            .Should().BeOfType<DecodedClubFrame.SubscribeDenied>();
    }

    [Fact]
    public async Task Dropping_EndsTheClubsSessions_WithTheChosenCode()
    {
        var (session, frames) = await SubscribeAsync(_clubA);
        _hub.IsLive(_clubA).Should().BeTrue();

        _controller.DropSocket(_clubA, 4100, "superseded");

        (await frames.MoveNextAsync().AsTask().WaitAsync(Timeout)).Should().BeFalse();
        session.Closure.Should().Be(new SocketClosure(4100, "superseded"));
        _hub.IsLive(_clubA).Should().BeFalse();
    }

    [Fact]
    public async Task ARawFrame_LetsAnUnusablePayloadBeTried()
    {
        var (_, frames) = await SubscribeAsync(_clubA);

        _controller.SendSocketFrame(_clubA, new SocketFrameRequest("ClubMemberJoined", """{"user":{"userId":"x"}}"""));

        ((DecodedClubFrame.Push)await NextAsync(frames, _clubA)).PayloadUnusable.Should().BeTrue();
    }

    [Fact]
    public async Task AChatMessage_IsStoredCutAt300_AndEchoedToTheChatsSubscribers()
    {
        var session = await new MockGeoGuessrSocketConnector(_hub)
            .ConnectAsync(new GeoGuessrClubEntry { ClubId = _clubA, NcfaToken = "x" }, false, CancellationToken.None);
        _sessions.Add(session);
        var frames = session.ReadFramesAsync(CancellationToken.None).GetAsyncEnumerator();
        var chat = SocketTopics.ClubChat(_clubA);

        await session.SendAsync(OutboundFrames.Subscribe(chat), CancellationToken.None);
        (await NextAsync(frames, _clubA)).Should().BeOfType<DecodedClubFrame.Subscribed>().Which.Topic.Should().Be(chat);

        await session.SendAsync(OutboundFrames.ChatMessage(chat, new string('x', 310)), CancellationToken.None);

        (await NextAsync(frames, _clubA)).Should().BeOfType<DecodedClubFrame.ChatMessage>().Which.Text.Should().HaveLength(300);
        _store.ClubChats[_clubA].Should().ContainSingle().Which.Echoed.Should().BeTrue();
    }

    [Fact]
    public async Task DroppedEchoes_StoreTheMessage_WithoutSendingItBack()
    {
        _store.ChatEchoesDropped[_clubA] = true;
        var session = await new MockGeoGuessrSocketConnector(_hub)
            .ConnectAsync(new GeoGuessrClubEntry { ClubId = _clubA, NcfaToken = "x" }, false, CancellationToken.None);
        _sessions.Add(session);
        var chat = SocketTopics.ClubChat(_clubA);
        await session.SendAsync(OutboundFrames.Subscribe(chat), CancellationToken.None);

        await session.SendAsync(OutboundFrames.ChatMessage(chat, "hi"), CancellationToken.None);

        _store.ClubChats[_clubA].Should().ContainSingle().Which.Echoed.Should().BeFalse();
    }
}
