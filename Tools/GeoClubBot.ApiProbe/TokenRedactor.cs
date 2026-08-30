using System.Text.RegularExpressions;

namespace GeoClubBot.ApiProbe;

/// <summary>
/// Keeps the <c>_ncfa</c> cookie out of anything the probe prints. Mirrors the bot's own
/// <c>GeoClubBot.Discord/Logging/LogRedactor.cs</c>; duplicated rather than referenced so this
/// project can stay dependency-free.
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

        return NcfaCookie().Replace(value, "_ncfa=" + Replacement);
    }

    [GeneratedRegex("""_ncfa=[^;\s"']+""", RegexOptions.CultureInvariant)]
    private static partial Regex NcfaCookie();
}
