using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Configuration;
using Infrastructure.InputAdapters.GeoGuessrSocket;
using Microsoft.Extensions.Time.Testing;
using UseCases.UseCases.ClubEvents;

namespace GeoClubBot.Tests.GeoGuessrSocket;

/// <summary>A socket the test feeds frames into and closes.</summary>
internal sealed class ScriptedSession : IGeoGuessrSocketSession
{
    private readonly Channel<string> _frames = Channel.CreateUnbounded<string>();

    public ConcurrentQueue<string> Sent { get; } = new();

    public SocketClosure? Closure { get; private set; }

    public bool Disposed { get; private set; }

    public void Feed(string frame) => _frames.Writer.TryWrite(frame);

    public void Close(int? code, string? description = null)
    {
        Closure = new SocketClosure(code, description);
        _frames.Writer.TryComplete();
    }

    public Task SendAsync(string frame, CancellationToken cancellationToken)
    {
        Sent.Enqueue(frame);
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
}

/// <summary>Hands out the scripted outcomes in order; once they run out, every attempt fails to connect.</summary>
internal sealed class ScriptedConnector : IGeoGuessrSocketConnector
{
    private readonly ConcurrentQueue<Func<IGeoGuessrSocketSession>> _outcomes = new();

    public ConcurrentQueue<bool> RestartFlags { get; } = new();

    public int Attempts => RestartFlags.Count;

    public ScriptedSession Session()
    {
        var session = new ScriptedSession();
        _outcomes.Enqueue(() => session);
        return session;
    }

    public void Throw(Exception exception) => _outcomes.Enqueue(() => throw exception);

    public Task<IGeoGuessrSocketSession> ConnectAsync(GeoGuessrClubEntry club, bool afterServerRestart, CancellationToken cancellationToken)
    {
        RestartFlags.Enqueue(afterServerRestart);
        return _outcomes.TryDequeue(out var outcome)
            ? Task.FromResult(outcome())
            : Task.FromException<IGeoGuessrSocketSession>(new WebSocketException("nothing scripted"));
    }
}

internal sealed class RecordingSink : IClubEventSink
{
    public ConcurrentQueue<ClubPushEvent> Events { get; } = new();

    public void Publish(ClubPushEvent pushEvent) => Events.Enqueue(pushEvent);
}

internal static class FakeClock
{
    /// <summary>
    /// Moves the fake clock on in small steps until <paramref name="condition"/> holds, giving the code
    /// under test real time between steps to react. Fails after <paramref name="maxFakeTime"/> of fake time.
    /// </summary>
    public static async Task EventuallyAsync(this FakeTimeProvider clock, Func<bool> condition, TimeSpan maxFakeTime, TimeSpan? step = null)
    {
        var advanced = TimeSpan.Zero;
        var stepSize = step ?? TimeSpan.FromMilliseconds(300);
        var realDeadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            if (advanced > maxFakeTime || DateTime.UtcNow > realDeadline)
            {
                throw new TimeoutException($"The condition didn't hold within {maxFakeTime} of fake time.");
            }

            await Task.Delay(2);
            clock.Advance(stepSize);
            advanced += stepSize;
        }
    }

    /// <summary>Advances the clock by <paramref name="total"/> in small steps, letting the code react between them.</summary>
    public static async Task AdvanceSlowlyAsync(this FakeTimeProvider clock, TimeSpan total, TimeSpan? step = null)
    {
        var stepSize = step ?? TimeSpan.FromSeconds(1);
        for (var advanced = TimeSpan.Zero; advanced < total; advanced += stepSize)
        {
            await Task.Delay(1);
            clock.Advance(stepSize);
        }
    }
}
