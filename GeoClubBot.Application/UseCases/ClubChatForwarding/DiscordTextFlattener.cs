using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace UseCases.UseCases.ClubChatForwarding;

/// <summary>Resolves the names a Discord message refers to by id. Null when the id is unknown.</summary>
/// <param name="User">A user's server display name.</param>
public sealed record DiscordNameLookup(
    Func<ulong, string?> User,
    Func<ulong, string?> Role,
    Func<ulong, string?> Channel);

/// <summary>
/// Turns a Discord message into readable plain text for GeoGuessr's chat, which shows text as it
/// is: mentions become <c>@name</c> / <c>#name</c>, custom emoji <c>:name:</c>, timestamps a UTC
/// date, and markdown markers (bold, italics, underline, strike-through, spoilers, code, headings,
/// quotes) are removed. Links are kept, a masked one as <c>label (url)</c>. Code, links and
/// resolved names are kept literally: nothing in them is read as markdown.
/// </summary>
public static partial class DiscordTextFlattener
{
    private const char Open = '';
    private const char Close = '';

    public static string Flatten(string content, DiscordNameLookup names)
    {
        var literals = new List<string>();
        string Keep(string literal)
        {
            literals.Add(literal);
            return string.Create(CultureInfo.InvariantCulture, $"{Open}{literals.Count - 1}{Close}");
        }

        // Literal content first, so nothing in it is touched by the markdown rules below.
        var text = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        text = CodeBlock().Replace(text, m => Keep(m.Groups["code"].Value.Trim('\n')));
        text = InlineCode().Replace(text, m => Keep(m.Groups["code"].Value));
        text = Escaped().Replace(text, m => Keep(m.Groups[1].Value));
        text = MaskedLink().Replace(text, m => $"{m.Groups["label"].Value} ({Keep(m.Groups["url"].Value)})");
        text = AngleLink().Replace(text, m => Keep(m.Groups["url"].Value));
        text = BareLink().Replace(text, m => Keep(m.Value));

        text = UserMention().Replace(text, m => Keep("@" + (names.User(Id(m)) ?? "unknown-user")));
        text = RoleMention().Replace(text, m => Keep("@" + (names.Role(Id(m)) ?? "unknown-role")));
        text = ChannelMention().Replace(text, m => Keep("#" + (names.Channel(Id(m)) ?? "unknown-channel")));
        text = SlashCommand().Replace(text, m => Keep("/" + m.Groups["name"].Value));
        text = CustomEmoji().Replace(text, m => Keep($":{m.Groups["name"].Value}:"));
        text = Timestamp().Replace(text, m => Keep(FormatTimestamp(m)));

        text = StripLineMarkers(text);
        text = PairedMarker().Replace(text, string.Empty);
        text = Italics().Replace(text, m => m.Groups["inner"].Value);

        text = Literal().Replace(text, m => literals[int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)]);
        text = ExtraBlankLines().Replace(text, "\n\n");
        return text.Trim();
    }

    /// <summary>Headings, subtext and quotes are markdown only at the start of a line.</summary>
    private static string StripLineMarkers(string text)
    {
        var result = new StringBuilder(text.Length);
        var inBlockQuote = false;
        foreach (var line in text.Split('\n'))
        {
            var stripped = line;
            if (stripped.StartsWith(">>> ", StringComparison.Ordinal))
            {
                // Everything to the end of the message is quoted.
                inBlockQuote = true;
                stripped = stripped[4..];
            }
            else if (!inBlockQuote && stripped.StartsWith("> ", StringComparison.Ordinal))
            {
                stripped = stripped[2..];
            }

            stripped = LineMarker().Replace(stripped, string.Empty);
            result.Append(stripped).Append('\n');
        }

        return result.ToString(0, result.Length - 1);
    }

    private static ulong Id(Match match) => ulong.Parse(match.Groups["id"].Value, CultureInfo.InvariantCulture);

    private static string FormatTimestamp(Match match)
    {
        if (!long.TryParse(match.Groups["seconds"].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var seconds)
            || seconds < -62_135_596_800 || seconds > 253_402_300_799)
        {
            return match.Value;
        }

        var at = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
        var format = match.Groups["style"].Value switch
        {
            "t" => "HH:mm 'UTC'",
            "T" => "HH:mm:ss 'UTC'",
            "d" or "D" => "yyyy-MM-dd",
            _ => "yyyy-MM-dd HH:mm 'UTC'",
        };

        return at.ToString(format, CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"```(?:[\w+-]*\n)?(?<code>[\s\S]*?)```")]
    private static partial Regex CodeBlock();

    [GeneratedRegex(@"(?<ticks>``?)(?<code>[^`]+?)\k<ticks>")]
    private static partial Regex InlineCode();

    [GeneratedRegex(@"\\([*_~|`>#\\\[\]()<:-])")]
    private static partial Regex Escaped();

    [GeneratedRegex(@"\[(?<label>[^\]\n]+)\]\(<?(?<url>https?://[^\s)>]+)>?\)")]
    private static partial Regex MaskedLink();

    [GeneratedRegex(@"<(?<url>https?://[^\s>]+)>")]
    private static partial Regex AngleLink();

    [GeneratedRegex(@"https?://[^\s<>]+")]
    private static partial Regex BareLink();

    [GeneratedRegex(@"<@!?(?<id>\d+)>")]
    private static partial Regex UserMention();

    [GeneratedRegex(@"<@&(?<id>\d+)>")]
    private static partial Regex RoleMention();

    [GeneratedRegex(@"<#(?<id>\d+)>")]
    private static partial Regex ChannelMention();

    [GeneratedRegex(@"</(?<name>[\w -]+):\d+>")]
    private static partial Regex SlashCommand();

    [GeneratedRegex(@"<a?:(?<name>\w+):\d+>")]
    private static partial Regex CustomEmoji();

    [GeneratedRegex(@"<t:(?<seconds>-?\d+)(?::(?<style>[tTdDfFR]))?>")]
    private static partial Regex Timestamp();

    /// <summary>Heading (<c># </c> to <c>### </c>) and subtext (<c>-# </c>) markers.</summary>
    [GeneratedRegex(@"^(?:#{1,3}|-#) +")]
    private static partial Regex LineMarker();

    [GeneratedRegex(@"\*\*|__|~~|\|\|")]
    private static partial Regex PairedMarker();

    /// <summary>A single <c>*</c> or <c>_</c> around text, not inside a word, so snake_case stays.</summary>
    [GeneratedRegex(@"(?<![\w*])(?<mark>[*_])(?<inner>\S(?:[^\n]*?\S)?)\k<mark>(?![\w*])")]
    private static partial Regex Italics();

    [GeneratedRegex("(\\d+)")]
    private static partial Regex Literal();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExtraBlankLines();
}
