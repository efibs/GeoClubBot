using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Configuration;
using FluentAssertions;
using Infrastructure.InputAdapters.GeoGuessrSocket;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.GeoGuessr;
using Xunit;

namespace GeoClubBot.Tests.GeoGuessrSocket;

public sealed class GeoGuessrClubChatSenderTests
{
    private static readonly Guid ClubId = Guid.Parse("6372d694-e433-4f69-8f7c-592ec127a577");
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);
    private static readonly string ChatTopic = $"chat:Club:TextMessages:{ClubId}";

    private readonly ClubChatChannels _chats = new();
    private readonly ChatConnector _connector = new();

    private GeoGuessrClubChatSender Sender() => new(
        _chats,
        _connector,
        Options.Create(new GeoGuessrConfiguration
        {
            SyncSchedule = "x",
            ActivityNcfaToken = "x",
            UserProfileNcfaToken = "x",
            Clubs = [new GeoGuessrClubEntry { ClubId = ClubId, NcfaToken = "t", IsMain = true }],
        }),
        NullLogger<GeoGuessrClubChatSender>.Instance);

    [Fact]
    public async Task WithoutALiveSocket_OpensOneForTheForward_SendsEachPartAfterItsEcho_AndClosesIt()
    {
        var session = _connector.Next();

        var delivery = await Sender().SendAsync(ClubId, ["(1/2) a", "(2/2) b"], Timeout);

        delivery.Should().Be(new ClubChatDelivery(ClubChatDeliveryStatus.Confirmed, 2, 2));
        session.Sent.Should().Equal(
            $$"""{"code":"Subscribe","topic":"{{ChatTopic}}","client":"web"}""",
            $$"""{"code":"ChatMessage","topic":"{{ChatTopic}}","payload":"(1/2) a","client":"web"}""",
            $$"""{"code":"ChatMessage","topic":"{{ChatTopic}}","payload":"(2/2) b","client":"web"}""");
        session.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task WithALiveSocket_UsesIt_AndSubscribesTheChatOnlyOnce()
    {
        var live = new ChatSession();
        using var attached = _chats.Attach(ClubId, live);
        using var reading = new CancellationTokenSource();
        var reader = ForwardFramesAsync(live, reading.Token);

        var sender = Sender();
        (await sender.SendAsync(ClubId, ["one"], Timeout)).Status.Should().Be(ClubChatDeliveryStatus.Confirmed);
        (await sender.SendAsync(ClubId, ["two"], Timeout)).Status.Should().Be(ClubChatDeliveryStatus.Confirmed);

        _connector.Attempts.Should().Be(0);
        live.Sent.Count(f => f.Contains("\"Subscribe\"", StringComparison.Ordinal)).Should().Be(1);
        await reading.CancelAsync();
        await reader;
    }

    [Fact]
    public async Task AMissingEcho_StopsTheForward_AndIsReportedUnconfirmed()
    {
        var session = _connector.Next();
        session.EchoUpTo = 1;

        // The timeout is the wait for each echo, so it must leave the echoed part room on a busy runner.
        var delivery = await Sender().SendAsync(ClubId, ["(1/3) a", "(2/3) b", "(3/3) c"], Timeout);

        delivery.Status.Should().Be(ClubChatDeliveryStatus.Unconfirmed);
        delivery.PartsSent.Should().Be(2);
        delivery.PartsConfirmed.Should().Be(1);
        session.Sent.Should().NotContain(f => f.Contains("(3/3)", StringComparison.Ordinal), "nothing goes out after an unconfirmed part");
    }

    [Fact]
    public async Task ADeniedChat_SendsNothing()
    {
        var session = _connector.Next();
        session.DenyChat = true;

        var delivery = await Sender().SendAsync(ClubId, ["a"], Timeout);

        delivery.Status.Should().Be(ClubChatDeliveryStatus.Denied);
        session.Sent.Should().ContainSingle().Which.Should().Contain("Subscribe");
    }

    [Fact]
    public async Task ARefusedConnection_IsReported_NotThrown()
    {
        _connector.Refuse(new SocketHandshakeException(401, null));

        var delivery = await Sender().SendAsync(ClubId, ["a"], Timeout);

        delivery.Status.Should().Be(ClubChatDeliveryStatus.NotConnected);
        delivery.PartsSent.Should().Be(0);
        delivery.Detail.Should().Contain("401");
    }

    [Fact]
    public async Task AnUnconfiguredClub_IsNotConnected()
    {
        (await Sender().SendAsync(Guid.NewGuid(), ["a"], Timeout)).Status.Should().Be(ClubChatDeliveryStatus.NotConnected);
        _connector.Attempts.Should().Be(0);
    }

    [Fact]
    public async Task AnUnansweredSubscription_StillSends_AndTheEchoDecides()
    {
        var session = _connector.Next();
        session.AcknowledgeSubscription = false;

        // The same timeout bounds the unanswered subscription and then the echo, which must arrive in it.
        var delivery = await Sender().SendAsync(ClubId, ["a"], Timeout);

        delivery.Status.Should().Be(ClubChatDeliveryStatus.Confirmed);
    }

    /// <summary>What the supervisor does for a live socket: hands its frames to the attached chat.</summary>
    private async Task ForwardFramesAsync(ChatSession session, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var frame in session.ReadFramesAsync(cancellationToken))
            {
                _chats.Offer(ClubId, ClubFrameDecoder.Decode(frame, ClubId));
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>A socket that answers like GeoGuessr: acknowledges the chat topic and echoes what is written into it.</summary>
    private sealed class ChatSession : IGeoGuessrSocketSession
    {
        private readonly Channel<string> _frames = Channel.CreateUnbounded<string>();
        private int _echoed;

        public ConcurrentQueue<string> Sent { get; } = new();

        public bool Disposed { get; private set; }

        public bool DenyChat { get; set; }

        public bool AcknowledgeSubscription { get; set; } = true;

        /// <summary>How many messages are echoed before the echoes stop.</summary>
        public int EchoUpTo { get; set; } = int.MaxValue;

        public SocketClosure? Closure => null;

        public Task SendAsync(string frame, CancellationToken cancellationToken)
        {
            Sent.Enqueue(frame);
            using var document = JsonDocument.Parse(frame);
            var root = document.RootElement;
            var topic = root.GetProperty("topic").GetString();
            switch (root.GetProperty("code").GetString())
            {
                case "Subscribe" when DenyChat:
                    Feed("SubscribeDenied", topic, null);
                    break;
                case "Subscribe" when AcknowledgeSubscription:
                    Feed("Subscribed", topic, """{"level":1}""");
                    break;
                case "ChatMessage" when _echoed++ < EchoUpTo:
                    Feed("ChatMessage", topic, JsonSerializer.Serialize(new
                    {
                        id = "abcdefghij",
                        textPayload = root.GetProperty("payload").GetString(),
                        sourceId = "bot",
                    }));
                    break;
            }

            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<string> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var frame in _frames.Reader.ReadAllAsync(cancellationToken))
            {
                yield return frame;
            }
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            _frames.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }

        private void Feed(string code, string? topic, string? payload) =>
            _frames.Writer.TryWrite(JsonSerializer.Serialize(new { code, topic, payload }));
    }

    private sealed class ChatConnector : IGeoGuessrSocketConnector
    {
        private readonly ConcurrentQueue<Func<IGeoGuessrSocketSession>> _outcomes = new();
        private int _attempts;

        public int Attempts => _attempts;

        public ChatSession Next()
        {
            var session = new ChatSession();
            _outcomes.Enqueue(() => session);
            return session;
        }

        public void Refuse(Exception exception) => _outcomes.Enqueue(() => throw exception);

        public Task<IGeoGuessrSocketSession> ConnectAsync(GeoGuessrClubEntry club, bool afterServerRestart, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _attempts);
            try
            {
                return _outcomes.TryDequeue(out var outcome)
                    ? Task.FromResult(outcome())
                    : Task.FromException<IGeoGuessrSocketSession>(new WebSocketException("nothing scripted"));
            }
            catch (Exception ex)
            {
                return Task.FromException<IGeoGuessrSocketSession>(ex);
            }
        }
    }
}
