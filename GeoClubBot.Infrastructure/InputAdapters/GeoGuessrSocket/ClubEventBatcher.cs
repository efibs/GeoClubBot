using System.Threading.Channels;
using Configuration;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.UseCases.ClubEvents;

namespace Infrastructure.InputAdapters.GeoGuessrSocket;

/// <summary>Where the clubs' sockets put what they receive.</summary>
public interface IClubEventSink
{
    void Publish(ClubPushEvent pushEvent);
}

/// <summary>
/// Collects the events of every club's socket and applies them in batches: from the first event, it
/// waits <see cref="GeoGuessrSocketConfiguration.BatchWindow"/>, then hands everything received to
/// <see cref="ApplyClubEventsCommand"/>. A switch arrives as a leave on one club's socket and a join on
/// the other's; inside one window the two are applied as the switch they are.
/// </summary>
public sealed partial class ClubEventBatcher(
    IServiceScopeFactory scopeFactory,
    IOptions<GeoGuessrSocketConfiguration> options,
    TimeProvider timeProvider,
    ILogger<ClubEventBatcher> logger) : IClubEventSink
{
    private readonly Channel<ClubPushEvent> _events =
        Channel.CreateUnbounded<ClubPushEvent>(new UnboundedChannelOptions { SingleReader = true });

    private long _sequence;

    /// <summary>Queues an event, numbering it in the order of arrival.</summary>
    public void Publish(ClubPushEvent pushEvent) =>
        _events.Writer.TryWrite(pushEvent with
        {
            Order = pushEvent.Order with { Sequence = Interlocked.Increment(ref _sequence) },
        });

    /// <summary>Applies batches until <paramref name="cancellationToken"/> is cancelled. A failed batch never stops it.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (await _events.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // The window starts at the first event and doesn't slide, so a steady trickle can't hold
            // a join back indefinitely.
            await Task.Delay(options.Value.BatchWindow, timeProvider, cancellationToken).ConfigureAwait(false);

            var batch = new List<ClubPushEvent>();
            while (_events.Reader.TryRead(out var pushEvent))
            {
                batch.Add(pushEvent);
            }

            await ApplyAsync(batch, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ApplyAsync(List<ClubPushEvent> batch, CancellationToken cancellationToken)
    {
        bool complete;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            complete = await scope.ServiceProvider.GetRequiredService<ISender>()
                .Send(new ApplyClubEventsCommand(batch), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogBatchFailed(ex, batch.Count);
            complete = false;
        }

        // What went wrong is unknown, so the clubs concerned are read again — once: a retry that fails
        // too is left to the polling and the scheduled jobs.
        if (complete || batch.Any(e => e is ClubRefreshRequested { Reason: ClubRefreshReason.BatchRetry }))
        {
            return;
        }

        foreach (var clubId in batch.Select(e => e.ClubId).Distinct())
        {
            Publish(new ClubRefreshRequested(clubId, default, ClubRefreshReason.BatchRetry));
        }
    }

    [LoggerMessage(LogLevel.Error, "Applying a batch of {EventCount} GeoGuessr socket events failed.")]
    partial void LogBatchFailed(Exception ex, int eventCount);
}
