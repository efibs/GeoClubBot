using Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoClubBot.Discord.Logging;

/// <summary>
/// <see cref="ILogger"/> for a single category that forwards qualifying logs to
/// <see cref="DiscordChannelLogQueue"/>. Does only cheap, non-throwing work on the caller's thread:
/// render the message and enqueue it.
/// </summary>
internal sealed class DiscordChannelLogger(
    string category,
    DiscordChannelLogQueue queue,
    IOptions<DiscordLoggingConfiguration> config) : ILogger
{
    // Whether this category may ever be forwarded. Computed once per category because excluding it
    // is what stops a feedback loop: delivering a log goes through Discord.Net and its HttpClient,
    // both of which emit their own logs — forwarding those would queue more work endlessly.
    private readonly bool _categoryAllowed = !IsExcludedCategory(category);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel)
    {
        var settings = config.Value;
        return _categoryAllowed
            && settings.Enabled
            && logLevel >= settings.MinimumLogLevel
            && logLevel != LogLevel.None;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);

        // One-line summary only — never the stack trace (those live in the regular logs).
        var exceptionLine = exception is null
            ? null
            : $"{exception.GetType().Name}: {exception.Message}";

        var entry = new DiscordLogEntry(
            DateTimeOffset.UtcNow,
            logLevel,
            category,
            message,
            exceptionLine);

        // Non-blocking; drops under sustained pressure rather than stalling the caller.
        queue.TryWrite(entry);
    }

    private static bool IsExcludedCategory(string category) =>
        // Discord.Net routes its own logs through categories like "Discord", "Discord.WebSocket",
        // "Discord.Rest"; the REST client logs under "System.Net.Http.*". Our own delivery code
        // lives under this namespace. Excluding all three prevents a self-feeding loop.
        category.StartsWith("Discord", StringComparison.Ordinal)
        || category.StartsWith("System.Net.Http", StringComparison.Ordinal)
        || category.StartsWith("GeoClubBot.Discord.Logging", StringComparison.Ordinal);

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
