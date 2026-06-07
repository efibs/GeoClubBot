using Discord;
using Microsoft.Extensions.Logging;

namespace GeoClubBot.Discord.Logging;

/// <summary>
/// Renders a <see cref="DiscordLogEntry"/> into a colour-coded embed: a level emoji + short source
/// in the title, the message (plus a one-line exception summary) as the body, and the full category
/// in the footer. Pure and side-effect free so it can be snapshot-tested.
/// </summary>
public sealed class DiscordLogEmbedFormatter
{
    // Discord caps: title 256, description 4096. Leave head-room on the description for the
    // appended exception line.
    private const int MaxTitleLength = 256;
    private const int MaxDescriptionLength = 3900;

    private static readonly Color WarningColor = new(0xE6, 0x7E, 0x22);
    private static readonly Color ErrorColor = new(0xE7, 0x4C, 0x3C);
    private static readonly Color CriticalColor = new(0x99, 0x2D, 0x22);
    private static readonly Color DefaultColor = new(0x95, 0xA5, 0xA6);

    public Embed Build(DiscordLogEntry entry)
    {
        var title = Truncate($"{LevelEmoji(entry.Level)} {entry.Level} · {ShortCategory(entry.Category)}",
            MaxTitleLength);

        // Redact before truncating so the scrubbed (not raw) text is what gets length-limited.
        var description = Truncate(LogRedactor.Redact(entry.Message), MaxDescriptionLength);
        if (entry.ExceptionLine is not null)
        {
            description = $"{description}\n{LogRedactor.Redact(entry.ExceptionLine)}";
        }

        return new EmbedBuilder()
            .WithTitle(title)
            .WithDescription(description)
            .WithColor(LevelColor(entry.Level))
            .WithFooter(entry.Category)
            .WithTimestamp(entry.Timestamp)
            .Build();
    }

    private static Color LevelColor(LogLevel level) => level switch
    {
        LogLevel.Critical => CriticalColor,
        LogLevel.Error => ErrorColor,
        LogLevel.Warning => WarningColor,
        _ => DefaultColor
    };

    private static string LevelEmoji(LogLevel level) => level switch
    {
        LogLevel.Critical => "🔥",
        LogLevel.Error => "❌",
        LogLevel.Warning => "⚠️",
        _ => "ℹ️"
    };

    private static string ShortCategory(string category)
    {
        var lastDot = category.LastIndexOf('.');
        return lastDot >= 0 && lastDot < category.Length - 1
            ? category[(lastDot + 1)..]
            : category;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
}
