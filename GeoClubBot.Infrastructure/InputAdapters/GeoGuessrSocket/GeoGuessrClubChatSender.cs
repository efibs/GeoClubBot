using System.Collections.Concurrent;
using System.Net.WebSockets;
using Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Observability;
using UseCases.OutputPorts.GeoGuessr;

namespace Infrastructure.InputAdapters.GeoGuessrSocket;

/// <summary>
/// Writes into a club's chat over GeoGuessr's socket, as the account of the club's token. Uses the
/// club's live socket when it is connected; otherwise opens one only for the forward and closes it
/// afterwards. Each part waits for its echo before the next goes out, and nothing is ever resent.
/// </summary>
/// <remarks>
/// The frame (<c>ChatMessage</c> on <c>chat:Club:TextMessages:&lt;clubId&gt;</c>) was observed for
/// direct messages; the club topic comes from GeoGuessr's client code. See
/// <c>Documentation/GeoGuessrSocket.md</c>.
/// </remarks>
public sealed partial class GeoGuessrClubChatSender(
    ClubChatChannels chats,
    IGeoGuessrSocketConnector connector,
    IOptions<GeoGuessrConfiguration> geoGuessrOptions,
    ILogger<GeoGuessrClubChatSender> logger) : IGeoGuessrClubChatSender
{
    /// <summary>Two admins forwarding at once would otherwise interleave their parts.</summary>
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _clubLocks = new();

    public async Task<ClubChatDelivery> SendAsync(Guid clubId, IReadOnlyList<string> parts, TimeSpan echoTimeout, CancellationToken cancellationToken = default)
    {
        if (geoGuessrOptions.Value.Clubs.FirstOrDefault(c => c.ClubId == clubId) is not { } club)
        {
            return new ClubChatDelivery(ClubChatDeliveryStatus.NotConnected, 0, 0, "the club is not configured");
        }

        var clubLock = _clubLocks.GetOrAdd(clubId, _ => new SemaphoreSlim(1, 1));
        await clubLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var live = chats.Live(clubId);
            var delivery = live is not null
                ? await DeliverAsync(live, parts, echoTimeout, cancellationToken).ConfigureAwait(false)
                : await DeliverOverOwnSocketAsync(club, parts, echoTimeout, cancellationToken).ConfigureAwait(false);

            GeoGuessrSocketMetrics.ChatForwards.Add(1,
                new KeyValuePair<string, object?>("status", delivery.Status.ToString()),
                new KeyValuePair<string, object?>("socket", live is not null ? "live" : "own"));
            return delivery;
        }
        finally
        {
            clubLock.Release();
        }
    }

    private async Task<ClubChatDelivery> DeliverOverOwnSocketAsync(
        GeoGuessrClubEntry club, IReadOnlyList<string> parts, TimeSpan echoTimeout, CancellationToken cancellationToken)
    {
        IGeoGuessrSocketSession session;
        try
        {
            session = await connector.ConnectAsync(club, false, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketHandshakeException ex)
        {
            LogConnectRefused(club.ClubId, ex.StatusCode);
            return new ClubChatDelivery(ClubChatDeliveryStatus.NotConnected, 0, 0, $"GeoGuessr refused the connection ({ex.StatusCode})");
        }
        catch (Exception ex) when (ex is WebSocketException or HttpRequestException
                                       || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogConnectFailed(club.ClubId, ex.Message);
            return new ClubChatDelivery(ClubChatDeliveryStatus.NotConnected, 0, 0, "could not connect to GeoGuessr");
        }

        await using (session.ConfigureAwait(false))
        {
            var channel = new ClubChatChannel(club.ClubId, session);
            using var reading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var reader = ReadAsync(session, channel, reading.Token);
            try
            {
                return await DeliverAsync(channel, parts, echoTimeout, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await reading.CancelAsync().ConfigureAwait(false);
                await reader.ConfigureAwait(false);
            }
        }
    }

    /// <summary>Reads the socket the sender opened itself, handing every frame to the chat. Never throws.</summary>
    private static async Task ReadAsync(IGeoGuessrSocketSession session, ClubChatChannel channel, CancellationToken cancellationToken)
    {
        // Off the caller's stack: the session may complete reads synchronously.
        await Task.Yield();
        try
        {
            await foreach (var frame in session.ReadFramesAsync(cancellationToken).ConfigureAwait(false))
            {
                channel.Offer(ClubFrameDecoder.Decode(frame, channel.ClubId));
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
            // Closed by the sender once done, or lost; either way nothing more will be echoed.
        }
        finally
        {
            channel.Close();
        }
    }

    private async Task<ClubChatDelivery> DeliverAsync(
        ClubChatChannel channel, IReadOnlyList<string> parts, TimeSpan echoTimeout, CancellationToken cancellationToken)
    {
        var sent = 0;
        var confirmed = 0;
        try
        {
            var subscription = await channel.EnsureSubscribedAsync(echoTimeout, cancellationToken).ConfigureAwait(false);
            if (subscription == ChatSubscription.Denied)
            {
                return new ClubChatDelivery(ClubChatDeliveryStatus.Denied, 0, 0, "GeoGuessr refused the club chat to the club's account");
            }

            if (subscription == ChatSubscription.Unanswered)
            {
                LogSubscriptionUnanswered(channel.ClubId);
            }

            foreach (var part in parts)
            {
                // Counted before sending: a send that throws may still have gone out.
                sent++;
                if (!await channel.SendAndConfirmAsync(part, echoTimeout, cancellationToken).ConfigureAwait(false))
                {
                    return new ClubChatDelivery(ClubChatDeliveryStatus.Unconfirmed, sent, confirmed, "GeoGuessr did not echo the message back in time");
                }

                confirmed++;
            }

            return new ClubChatDelivery(ClubChatDeliveryStatus.Confirmed, sent, confirmed);
        }
        catch (ObjectDisposedException) when (sent > 0 && confirmed == sent - 1)
        {
            // The socket closed before the part went out.
            return new ClubChatDelivery(ClubChatDeliveryStatus.NotConnected, sent - 1, confirmed, "the connection to GeoGuessr closed");
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException
                                       || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogConnectionLost(channel.ClubId, ex.Message);
            return new ClubChatDelivery(ClubChatDeliveryStatus.NotConnected, sent, confirmed, "the connection to GeoGuessr was lost");
        }
    }

    [LoggerMessage(LogLevel.Warning, "Forwarding to the chat of club {ClubId}: GeoGuessr refused the socket with status {StatusCode}.")]
    partial void LogConnectRefused(Guid clubId, int statusCode);

    [LoggerMessage(LogLevel.Warning, "Forwarding to the chat of club {ClubId}: could not connect to GeoGuessr's socket: {Reason}")]
    partial void LogConnectFailed(Guid clubId, string reason);

    [LoggerMessage(LogLevel.Warning, "Forwarding to the chat of club {ClubId}: the socket was lost while sending: {Reason}")]
    partial void LogConnectionLost(Guid clubId, string reason);

    [LoggerMessage(LogLevel.Information, "GeoGuessr did not answer the chat subscription of club {ClubId}; sending anyway.")]
    partial void LogSubscriptionUnanswered(Guid clubId);
}
