using System.Collections.Concurrent;
using Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoClubBot.Discord.Logging;

/// <summary>
/// Registers the Discord channel sink with the logging pipeline. Created per category logger,
/// each forwarding to the shared <see cref="DiscordChannelLogQueue"/>.
/// </summary>
[ProviderAlias("DiscordChannel")]
public sealed class DiscordChannelLoggerProvider(
    DiscordChannelLogQueue queue,
    IOptions<DiscordLoggingConfiguration> config) : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, DiscordChannelLogger> _loggers = new(StringComparer.Ordinal);

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new DiscordChannelLogger(name, queue, config));

    public void Dispose() => _loggers.Clear();
}
