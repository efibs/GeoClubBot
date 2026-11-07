using System.Collections.Concurrent;

namespace Infrastructure.InputAdapters.GeoGuessrSocket;

/// <summary>What GeoGuessr answered to the subscription of a club's chat topic.</summary>
public enum ChatSubscription
{
    Subscribed,

    Denied,

    /// <summary>No answer in time. Sending is tried anyway; the echo then tells whether it arrived.</summary>
    Unanswered,
}

/// <summary>
/// A club's chat over one open socket: subscribes its topic once, sends messages, and waits for
/// GeoGuessr to echo each back. The frames read from the socket are handed in through
/// <see cref="Offer"/> by whoever reads it: the supervisor for a live socket, the sender for one it
/// opened itself.
/// </summary>
public sealed class ClubChatChannel(Guid clubId, IGeoGuessrSocketSession session)
{
    private readonly ConcurrentDictionary<PendingEcho, byte> _pending = new();
    private readonly TaskCompletionSource<ChatSubscription> _subscription = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _subscribing = new(1, 1);
    private bool _subscribeSent;
    private volatile bool _closed;

    public Guid ClubId { get; } = clubId;

    /// <summary>Subscribes the chat topic unless that was done on this socket already, and waits for the answer.</summary>
    public async Task<ChatSubscription> EnsureSubscribedAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        await _subscribing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_subscribeSent)
            {
                await session.SendAsync(OutboundFrames.Subscribe(SocketTopics.ClubChat(ClubId)), cancellationToken).ConfigureAwait(false);
                _subscribeSent = true;
            }
        }
        finally
        {
            _subscribing.Release();
        }

        try
        {
            return await _subscription.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return ChatSubscription.Unanswered;
        }
    }

    /// <summary>Sends <paramref name="text"/> and waits for its echo.</summary>
    /// <returns>Whether GeoGuessr echoed it back in time.</returns>
    /// <exception cref="ObjectDisposedException">The socket closed before the text was sent.</exception>
    public async Task<bool> SendAndConfirmAsync(string text, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_closed, this);

        // Waiting starts before sending: the echo can arrive before SendAsync returns.
        var pending = new PendingEcho(Normalise(text));
        _pending[pending] = 0;
        try
        {
            await session.SendAsync(OutboundFrames.ChatMessage(SocketTopics.ClubChat(ClubId), text), cancellationToken).ConfigureAwait(false);
            return await pending.Echoed.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return false;
        }
        finally
        {
            _pending.TryRemove(pending, out _);
        }
    }

    /// <summary>Takes a frame read from the socket. Frames about anything but the chat are ignored.</summary>
    public void Offer(DecodedClubFrame frame)
    {
        switch (frame)
        {
            case DecodedClubFrame.ChatMessage message:
                // Messages only arrive once subscribed, whether or not the acknowledgement was seen.
                _subscription.TrySetResult(ChatSubscription.Subscribed);
                var text = Normalise(message.Text);
                var match = _pending.Keys.FirstOrDefault(p => p.Text == text && !p.Echoed.Task.IsCompleted);
                match?.Echoed.TrySetResult(true);
                break;

            case DecodedClubFrame.Subscribed subscribed when IsChatTopic(subscribed.Topic):
                _subscription.TrySetResult(ChatSubscription.Subscribed);
                break;

            case DecodedClubFrame.SubscribeDenied denied when IsChatTopic(denied.Topic):
                _subscription.TrySetResult(ChatSubscription.Denied);
                break;
        }
    }

    /// <summary>The socket is gone: whatever still waits for an echo won't get one.</summary>
    public void Close()
    {
        _closed = true;
        foreach (var pending in _pending.Keys)
        {
            pending.Echoed.TrySetResult(false);
        }
    }

    public bool IsChatTopic(string? topic) =>
        string.Equals(topic, SocketTopics.ClubChat(ClubId), StringComparison.OrdinalIgnoreCase);

    /// <summary>GeoGuessr stores text as sent; this only keeps line endings and outer whitespace from mattering.</summary>
    private static string Normalise(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private sealed class PendingEcho(string text)
    {
        public string Text { get; } = text;

        public TaskCompletionSource<bool> Echoed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

/// <summary>
/// The chat of each club whose live socket is open, so a forward can go out on it instead of a
/// connection of its own. The supervisor attaches its session when connected and detaches it when
/// the socket ends; it hands every chat frame it reads to the attached channel.
/// </summary>
public sealed class ClubChatChannels
{
    private readonly ConcurrentDictionary<Guid, ClubChatChannel> _live = new();

    public ClubChatChannel? Live(Guid clubId) => _live.GetValueOrDefault(clubId);

    /// <summary>Hands a frame read on the club's live socket to its chat, if one is attached.</summary>
    public void Offer(Guid clubId, DecodedClubFrame frame) => Live(clubId)?.Offer(frame);

    /// <returns>Detaches the channel and fails whatever still waits on it.</returns>
    public IDisposable Attach(Guid clubId, IGeoGuessrSocketSession session)
    {
        var channel = new ClubChatChannel(clubId, session);
        _live[clubId] = channel;
        return new Detacher(this, channel);
    }

    private void Detach(ClubChatChannel channel)
    {
        _live.TryRemove(new KeyValuePair<Guid, ClubChatChannel>(channel.ClubId, channel));
        channel.Close();
    }

    private sealed class Detacher(ClubChatChannels owner, ClubChatChannel channel) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Detach(channel);
            }
        }
    }
}
