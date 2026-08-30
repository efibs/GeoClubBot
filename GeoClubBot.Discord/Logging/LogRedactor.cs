using System.Text.RegularExpressions;

namespace GeoClubBot.Discord.Logging;

/// <summary>
/// Scrubs known secret shapes (auth tokens, API keys, the GeoGuessr <c>_ncfa</c> cookie,
/// connection-string passwords) out of text before it is forwarded to a Discord channel by the
/// logging sink. This is defence in depth — the real fix is never logging secrets — but it stops
/// an accidental leak (e.g. a failed HTTP call echoing the cookie) from being published to a
/// channel. Pure and side-effect free.
/// </summary>
public static partial class LogRedactor
{
    private const string Replacement = "***REDACTED***";

    public static string Redact(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        // Most specific patterns first; each pass is idempotent on already-redacted text.
        value = NcfaCookie().Replace(value, "_ncfa=" + Replacement);
        value = DiscordToken().Replace(value, Replacement);
        value = CerebrasKey().Replace(value, Replacement);
        value = KeyedSecret().Replace(value, static m => m.Groups["k"].Value + m.Groups["sep"].Value + Replacement);
        return value;
    }

    // GeoGuessr _ncfa cookie value, up to a cookie/whitespace delimiter.
    [GeneratedRegex("""_ncfa=[^;\s"']+""", RegexOptions.CultureInvariant)]
    private static partial Regex NcfaCookie();

    // Discord bot token: three base64url-ish segments separated by dots.
    [GeneratedRegex(@"[A-Za-z0-9_-]{24,}\.[A-Za-z0-9_-]{6,}\.[A-Za-z0-9_-]{27,}", RegexOptions.CultureInvariant)]
    private static partial Regex DiscordToken();

    // Cerebras-style API key.
    [GeneratedRegex("csk-[A-Za-z0-9]{8,}", RegexOptions.CultureInvariant)]
    private static partial Regex CerebrasKey();

    // Generic "<keyword> = <value>" / "<keyword>: <value>" assignments — keeps the key, redacts
    // the value. Covers api key / secret / token / password (e.g. connection-string Password=...).
    [GeneratedRegex(
        """(?<k>(?i:api[_-]?key|apikey|secret|token|password))(?<sep>"?\s*[:=]\s*"?)(?<v>[^"'\s,;}]+)""",
        RegexOptions.CultureInvariant)]
    private static partial Regex KeyedSecret();
}
