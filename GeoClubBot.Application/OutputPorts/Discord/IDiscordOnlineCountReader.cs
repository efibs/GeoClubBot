namespace UseCases.OutputPorts.Discord;

/// <summary>The server's online member count, and when it was read.</summary>
public sealed record DiscordOnlineCount(int Online, DateTimeOffset ReadAt);

/// <summary>
/// Reads the server's online member count, cached briefly because every visit of the club website
/// asks for it.
/// </summary>
public interface IDiscordOnlineCountReader
{
    /// <summary>
    /// The count; null when Discord could not be read. A failure is not cached, so the next call
    /// tries again.
    /// </summary>
    Task<DiscordOnlineCount?> ReadOnlineCountAsync(CancellationToken cancellationToken = default);
}
