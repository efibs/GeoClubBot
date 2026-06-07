using Microsoft.Extensions.Logging;

namespace Configuration;

/// <summary>
/// Optional sink that forwards application logs to a Discord channel so failures get noticed.
/// Disabled unless <see cref="ChannelId"/> is set, so it is bound without start-up validation
/// (mirrors <c>AiConfiguration</c>).
/// </summary>
public class DiscordLoggingConfiguration
{
    public const string SectionName = "DiscordLogging";

    /// <summary>
    /// Channel the logs are sent to. When <c>0</c> / unset the sink is disabled.
    /// </summary>
    public ulong ChannelId { get; set; }

    /// <summary>
    /// Inclusive minimum level forwarded to the channel. Logs at this level and above are sent.
    /// </summary>
    public LogLevel MinimumLogLevel { get; set; } = LogLevel.Warning;

    public bool Enabled => ChannelId != 0;
}
