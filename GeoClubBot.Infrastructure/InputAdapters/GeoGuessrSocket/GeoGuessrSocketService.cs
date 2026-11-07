using Configuration;
using GeoClubBot.Discord.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.UseCases.ClubEvents;

namespace Infrastructure.InputAdapters.GeoGuessrSocket;

/// <summary>
/// Holds one GeoGuessr socket per club that uses it (<see cref="GeoGuessrClubEntry.GetUseSocket"/>) and
/// applies what they receive through <see cref="ClubEventBatcher"/>. A club without a socket is
/// refreshed from the API every <see cref="GeoGuessrClubEntry.GetSocketPollWhileDisconnected"/> instead,
/// since the scheduled jobs only run every few hours.
/// </summary>
/// <remarks>
/// Waits for Discord first: the events it applies change roles and channels. Nothing in here may end
/// the host, so every failure is logged and the loops carry on.
/// </remarks>
public sealed partial class GeoGuessrSocketService(
    DiscordBotReadyService botReady,
    ClubEventBatcher batcher,
    IGeoGuessrSocketConnector connector,
    SocketFrameReports reports,
    ClubChatChannels chats,
    IOptions<GeoGuessrSocketConfiguration> socketOptions,
    IOptions<GeoGuessrConfiguration> geoGuessrOptions,
    TimeProvider timeProvider,
    ILoggerFactory loggerFactory,
    ILogger<GeoGuessrSocketService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await botReady.DiscordSocketClientReady.WaitAsync(stoppingToken).ConfigureAwait(false);

            var socket = socketOptions.Value;
            var loops = new List<Task> { batcher.RunAsync(stoppingToken) };
            foreach (var club in geoGuessrOptions.Value.Clubs)
            {
                if (club.GetUseSocket(socket))
                {
                    var supervisor = new ClubSocketSupervisor(
                        club, connector, batcher, reports, chats, socket, timeProvider,
                        loggerFactory.CreateLogger<ClubSocketSupervisor>());
                    loops.Add(supervisor.RunAsync(stoppingToken));
                }
                else
                {
                    loops.Add(PollAsync(club, club.GetSocketPollWhileDisconnected(socket), stoppingToken));
                }
            }

            LogStarted(loops.Count - 1, geoGuessrOptions.Value.Clubs.Count(c => c.GetUseSocket(socket)));
            await Task.WhenAll(loops).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            LogStopped(ex);
        }
    }

    /// <summary>A club without a socket, refreshed on a timer.</summary>
    private async Task PollAsync(GeoGuessrClubEntry club, TimeSpan interval, CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            batcher.Publish(new ClubRefreshRequested(club.ClubId, default, ClubRefreshReason.SocketDisabled));
        }
    }

    [LoggerMessage(LogLevel.Information, "GeoGuessr club events: {Clubs} clubs, {Sockets} of them over the socket.")]
    partial void LogStarted(int clubs, int sockets);

    [LoggerMessage(LogLevel.Error, "GeoGuessr club events stopped unexpectedly; joins and leaves wait for the scheduled sync.")]
    partial void LogStopped(Exception ex);
}
