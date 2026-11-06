using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GeoClubBot.Tests.GeoGuessrSocket;

/// <summary>
/// A real local WebSocket server, so the tests exercise a real <see cref="ClientWebSocket"/>. Kestrel
/// speaks plain ws here, which the settings allow for exactly this.
/// </summary>
internal sealed class FakeGeoGuessrSocketServer : IAsyncDisposable
{
    private readonly WebApplication _application;

    private FakeGeoGuessrSocketServer(WebApplication application) => _application = application;

    public string SocketUrl { get; private set; } = null!;

    public Dictionary<string, string>? LastHandshakeHeaders { get; private set; }

    public string? LastQuery { get; private set; }

    /// <param name="handler">Called twice per request: first with no socket, to refuse the handshake if it wants to.</param>
    public static async Task<FakeGeoGuessrSocketServer> StartAsync(Func<HttpContext, WebSocket?, Task> handler)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        var application = builder.Build();
        application.UseWebSockets();

        var server = new FakeGeoGuessrSocketServer(application);
        application.Map("/ws", async context =>
        {
            // Kestrel recycles the header collection after the request, so it is copied.
            server.LastHandshakeHeaders = context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());
            server.LastQuery = context.Request.QueryString.Value;

            await handler(context, null);
            if (!context.Response.HasStarted && context.Response.StatusCode == StatusCodes.Status200OK)
            {
                using var socket = await context.WebSockets.AcceptWebSocketAsync();
                await handler(context, socket);
            }
        });

        await application.StartAsync();

        var address = application.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        server.SocketUrl = $"{address.Replace("http://", "ws://", StringComparison.Ordinal)}/ws";
        return server;
    }

    public static async Task SendTextAsync(WebSocket socket, string text, int fragmentSize = int.MaxValue)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        for (var offset = 0; offset < bytes.Length; offset += fragmentSize)
        {
            var count = Math.Min(fragmentSize, bytes.Length - offset);
            var isLast = offset + count >= bytes.Length;
            await socket.SendAsync(bytes.AsMemory(offset, count), WebSocketMessageType.Text, isLast, CancellationToken.None);
        }
    }

    /// <summary>Closes from the server's side and waits for the client's answer.</summary>
    public static async Task CloseAsync(WebSocket socket, int code, string? description)
    {
        await socket.CloseOutputAsync((WebSocketCloseStatus)code, description, CancellationToken.None);
        var buffer = new byte[64];
        while (socket.State == WebSocketState.CloseSent)
        {
            await socket.ReceiveAsync(buffer, CancellationToken.None);
        }
    }

    public async ValueTask DisposeAsync() => await _application.DisposeAsync();
}
