using System.Threading.Channels;

namespace GeoClubBot.Discord.Logging;

/// <summary>
/// Bounded, thread-safe hand-off between the loggers (many writers) and the single background
/// processor that delivers entries to Discord. Writing is non-blocking: under a log storm the
/// oldest queued entries are dropped (<see cref="BoundedChannelFullMode.DropOldest"/>) rather than
/// blocking the calling code or growing memory without bound.
/// </summary>
public sealed class DiscordChannelLogQueue
{
    private readonly Channel<DiscordLogEntry> _channel = Channel.CreateBounded<DiscordLogEntry>(
        new BoundedChannelOptions(capacity: 500)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

    public bool TryWrite(DiscordLogEntry entry) => _channel.Writer.TryWrite(entry);

    public ChannelReader<DiscordLogEntry> Reader => _channel.Reader;
}
