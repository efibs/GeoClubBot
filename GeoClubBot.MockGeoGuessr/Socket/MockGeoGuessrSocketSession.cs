using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Infrastructure.InputAdapters.GeoGuessrSocket;

namespace GeoClubBot.MockGeoGuessr.Socket;

/// <summary>One bot connection to the mock's socket.</summary>
public sealed class MockGeoGuessrSocketSession(MockClubSocketHub hub) : IGeoGuessrSocketSession
{
    private readonly Channel<string> _frames = Channel.CreateUnbounded<string>();
    private readonly HashSet<string> _topics = [];
    private readonly Lock _topicsLock = new();

    public SocketClosure? Closure { get; private set; }

    public Task SendAsync(string frame, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(frame);
        var root = document.RootElement;
        if (root.TryGetProperty("code", out var code) && code.GetString() == "Subscribe")
        {
            hub.Subscribe(this, root.TryGetProperty("topic", out var topic) ? topic.GetString() : null);
        }

        // HeartBeat: the real server doesn't answer it either.
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<string> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var frame in _frames.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return frame;
        }

        Closure ??= new SocketClosure(1000, null);
    }

    public ValueTask DisposeAsync()
    {
        hub.Remove(this);
        _frames.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    internal bool IsSubscribedTo(string topic)
    {
        lock (_topicsLock)
        {
            return _topics.Contains(topic);
        }
    }

    internal void AddTopic(string topic)
    {
        lock (_topicsLock)
        {
            _topics.Add(topic);
        }
    }

    internal void Deliver(string frame) => _frames.Writer.TryWrite(frame);

    internal void CloseFromServer(int? closeCode, string? description)
    {
        Closure = new SocketClosure(closeCode, description);
        hub.Remove(this);
        _frames.Writer.TryComplete();
    }
}
