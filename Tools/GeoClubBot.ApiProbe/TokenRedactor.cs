using System.Text.RegularExpressions;

namespace GeoClubBot.ApiProbe;

/// <summary>
/// Keeps the <c>_ncfa</c> cookie out of anything the probe prints. Mirrors the bot's own
/// <c>GeoClubBot.Discord/Logging/LogRedactor.cs</c>; duplicated rather than referenced so this
/// project can stay dependency-free. Socket frames also carry an <c>accessToken</c>, which is
/// blanked the same way.
/// </summary>
public static partial class TokenRedactor
{
    private const string Replacement = "***REDACTED***";

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        var withoutCookie = NcfaCookie().Replace(value, "_ncfa=" + Replacement);
        return AccessToken().Replace(withoutCookie, $"$1\"{Replacement}\"");
    }

    [GeneratedRegex("""_ncfa=[^;\s"']+""", RegexOptions.CultureInvariant)]
    private static partial Regex NcfaCookie();

    // A regular string: a raw literal can't end in the value's closing quote.
    [GeneratedRegex("(\"accessToken\"\\s*:\\s*)\"(?:[^\"\\\\]|\\\\.)*\"", RegexOptions.CultureInvariant)]
    private static partial Regex AccessToken();
}
