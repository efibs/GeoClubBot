using Microsoft.Extensions.Logging;

namespace GeoClubBot.Discord.Logging;

/// <summary>
/// Snapshot of a single log captured at log time and queued for delivery to Discord. Holds only
/// rendered, immutable values so it can cross threads safely. <see cref="ExceptionLine"/> is a
/// single-line "Type: Message" summary — never a stack trace.
/// </summary>
public readonly record struct DiscordLogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    string Message,
    string? ExceptionLine);
