using System.Net.WebSockets;
using System.Text;
using Configuration;
using FluentAssertions;
using Infrastructure.InputAdapters.GeoGuessrSocket;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeoClubBot.Tests.GeoGuessrSocket;

/// <summary>The real connector and session against a real local WebSocket server.</summary>
public sealed class ClientWebSocketSessionTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private static readonly GeoGuessrClubEntry Club = new() { ClubId = Guid.NewGuid(), NcfaToken = "club%2Ftoken%3D" };

    private static ClientWebSocketConnector Connector(string socketUrl, int maxFrameBytes = 1024 * 1024) =>
        new(Options.Create(new GeoGuessrSocketConfiguration { Url = socketUrl, MaxFrameBytes = maxFrameBytes }));

    private static async Task<List<string>> ReadAllAsync(IGeoGuessrSocketSession session)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        var frames = new List<string>();
        await foreach (var frame in session.ReadFramesAsync(timeout.Token))
        {
            frames.Add(frame);
        }

        return frames;
    }

    private static Task<FakeGeoGuessrSocketServer> ServerThatClosesAsync(int code = 1000, string? description = null) =>
        FakeGeoGuessrSocketServer.StartAsync(async (_, socket) =>
        {
            if (socket is not null)
            {
                await FakeGeoGuessrSocketServer.CloseAsync(socket, code, description);
            }
        });

    [Fact]
    public async Task Handshake_SendsTheClubsSessionTheWayTheSiteDoes()
    {
        await using var server = await ServerThatClosesAsync();

        await using (var session = await Connector(server.SocketUrl).ConnectAsync(Club, true, CancellationToken.None))
        {
            await ReadAllAsync(session);
        }

        server.LastHandshakeHeaders!["Cookie"].Should().Be("_ncfa=club%2Ftoken%3D", "the token is sent exactly as configured");
        server.LastHandshakeHeaders["Origin"].Should().Be("https://www.geoguessr.com");
        server.LastHandshakeHeaders["User-Agent"].Should().Be("GeoClubBot (+https://github.com/efibs/GeoClubBot)");
        server.LastQuery.Should().Contain("c=web-1.8700-39ce1da").And.Contain("tabId=").And.Contain("attempt=1")
            .And.Contain("visibility=visible").And.Contain("reconnect=true");
    }

    [Fact]
    public async Task FirstConnection_DoesNotClaimToBeAReconnect()
    {
        await using var server = await ServerThatClosesAsync();

        await using (var session = await Connector(server.SocketUrl).ConnectAsync(Club, false, CancellationToken.None))
        {
            await ReadAllAsync(session);
        }

        server.LastQuery.Should().NotContain("reconnect");
    }

    [Fact]
    public async Task FragmentedFrame_ArrivesWhole_MultibyteTextIncluded()
    {
        var text = string.Concat(Enumerable.Repeat("✅ü", 20_000));
        await using var server = await FakeGeoGuessrSocketServer.StartAsync(async (_, socket) =>
        {
            if (socket is not null)
            {
                await FakeGeoGuessrSocketServer.SendTextAsync(socket, text, fragmentSize: 7_001);
                await FakeGeoGuessrSocketServer.CloseAsync(socket, 1000, null);
            }
        });

        await using var session = await Connector(server.SocketUrl).ConnectAsync(Club, false, CancellationToken.None);

        (await ReadAllAsync(session)).Should().ContainSingle().Which.Should().Be(text);
    }

    [Fact]
    public async Task OversizedFrame_EndsTheSession_InsteadOfGrowingForever()
    {
        await using var server = await FakeGeoGuessrSocketServer.StartAsync(async (_, socket) =>
        {
            if (socket is not null)
            {
                await FakeGeoGuessrSocketServer.SendTextAsync(socket, new string('x', 200_000));
                await socket.ReceiveAsync(new byte[16], CancellationToken.None);
            }
        });

        await using var session = await Connector(server.SocketUrl, maxFrameBytes: 50_000).ConnectAsync(Club, false, CancellationToken.None);

        (await ReadAllAsync(session)).Should().BeEmpty();
        session.Closure!.Description.Should().Be("frame too big");
    }

    [Fact]
    public async Task BinaryFrames_AreSkipped()
    {
        await using var server = await FakeGeoGuessrSocketServer.StartAsync(async (_, socket) =>
        {
            if (socket is not null)
            {
                await socket.SendAsync(new byte[] { 1, 2, 3 }, WebSocketMessageType.Binary, true, CancellationToken.None);
                await FakeGeoGuessrSocketServer.SendTextAsync(socket, "after");
                await FakeGeoGuessrSocketServer.CloseAsync(socket, 1000, null);
            }
        });

        await using var session = await Connector(server.SocketUrl).ConnectAsync(Club, false, CancellationToken.None);

        (await ReadAllAsync(session)).Should().Equal("after");
    }

    [Theory]
    [InlineData(4100, "superseded")]
    [InlineData(1012, null)]
    [InlineData(1001, "Unauthorized")]
    public async Task ServerClose_ReportsItsCodeAndReason(int code, string? description)
    {
        await using var server = await ServerThatClosesAsync(code, description);

        await using var session = await Connector(server.SocketUrl).ConnectAsync(Club, false, CancellationToken.None);
        await ReadAllAsync(session);

        session.Closure!.Code.Should().Be(code);
        (session.Closure.Description ?? "").Should().Be(description ?? "");
    }

    [Fact]
    public async Task AConnectionDroppedWithoutClosing_IsATransportFailure()
    {
        await using var server = await FakeGeoGuessrSocketServer.StartAsync(async (context, socket) =>
        {
            if (socket is null)
            {
                return;
            }

            // Dropped only once the client is connected: aborting straight after accepting can reset
            // the connection before the client has read the handshake, which fails the connect instead.
            await socket.ReceiveAsync(new byte[64], CancellationToken.None);
            socket.Abort();
        });

        await using var session = await Connector(server.SocketUrl).ConnectAsync(Club, false, CancellationToken.None);
        await session.SendAsync("""{"code":"HeartBeat"}""", CancellationToken.None);
        await ReadAllAsync(session);

        session.Closure!.Code.Should().BeNull();
    }

    [Theory]
    [InlineData(StatusCodes.Status401Unauthorized)]
    [InlineData(StatusCodes.Status403Forbidden)]
    public async Task RefusedHandshake_ReportsItsStatus(int status)
    {
        await using var server = await FakeGeoGuessrSocketServer.StartAsync((context, _) =>
        {
            context.Response.StatusCode = status;
            return context.Response.WriteAsync("no");
        });

        var act = () => Connector(server.SocketUrl).ConnectAsync(Club, false, CancellationToken.None);

        (await act.Should().ThrowAsync<SocketHandshakeException>()).Which.StatusCode.Should().Be(status);
    }

    [Fact]
    public async Task RefusedHandshake_ReportsRetryAfter()
    {
        await using var server = await FakeGeoGuessrSocketServer.StartAsync((context, _) =>
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = "120";
            return context.Response.WriteAsync("slow down");
        });

        var act = () => Connector(server.SocketUrl).ConnectAsync(Club, false, CancellationToken.None);

        (await act.Should().ThrowAsync<SocketHandshakeException>()).Which.RetryAfter.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task Send_ReachesTheServer()
    {
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = await FakeGeoGuessrSocketServer.StartAsync(async (_, socket) =>
        {
            if (socket is not null)
            {
                var buffer = new byte[1024];
                var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                received.SetResult(Encoding.UTF8.GetString(buffer, 0, result.Count));
                await FakeGeoGuessrSocketServer.CloseAsync(socket, 1000, null);
            }
        });

        await using var session = await Connector(server.SocketUrl).ConnectAsync(Club, false, CancellationToken.None);
        await session.SendAsync(OutboundFrames.Subscribe(SocketTopics.Club(Club.ClubId)), CancellationToken.None);

        (await received.Task.WaitAsync(TestTimeout)).Should()
            .Be($$"""{"code":"Subscribe","topic":"club:{{Club.ClubId:D}}","client":"web"}""");
    }

    [Fact]
    public void TheHeartbeat_IsTheSitesFrame() =>
        OutboundFrames.HeartBeat.Should().Be("""{"code":"HeartBeat"}""");
}
