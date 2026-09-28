using System.Globalization;
using System.Text.RegularExpressions;

namespace UseCases.UseCases.CountryChallenges;

/// <summary>
/// One line of an import: a player, named by GeoGuessr user id (from a profile link or the bare id) or
/// by nickname, and their points.
/// </summary>
public sealed record StandingsImportLine(int LineNumber, string Player, string? UserId, int Points)
{
    public bool IsNickname => UserId is null;
}

/// <summary>Why a line cannot be imported.</summary>
public sealed record StandingsImportProblem(int LineNumber, string Message)
{
    public override string ToString() => $"Line {LineNumber}: {Message}";
}

public sealed record StandingsImportParse(IReadOnlyList<StandingsImportLine> Lines, IReadOnlyList<StandingsImportProblem> Errors);

/// <summary>
/// Reads standings pasted by an admin, one player per line: <c>&lt;player&gt; &lt;points&gt;</c>. It
/// accepts what a hand-kept list tends to look like — a leading "1." rank, a ":" or "-" before the
/// points, a trailing "pts" — and skips blank lines and lines starting with <c>#</c>.
/// </summary>
public static partial class StandingsImportParser
{
    public static StandingsImportParse Parse(string text)
    {
        var lines = new List<StandingsImportLine>();
        var errors = new List<StandingsImportProblem>();
        var rawLines = text.Split('\n');

        for (var i = 0; i < rawLines.Length; i++)
        {
            var lineNumber = i + 1;
            var raw = rawLines[i].Trim();

            if (raw.Length == 0 || raw.StartsWith('#'))
            {
                continue;
            }

            var match = LineRegex().Match(raw);
            if (!match.Success)
            {
                errors.Add(new StandingsImportProblem(lineNumber, $"'{raw}' is not '<player> <points>'."));
                continue;
            }

            if (!int.TryParse(match.Groups["points"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var points))
            {
                errors.Add(new StandingsImportProblem(lineNumber, $"'{match.Groups["points"].Value}' is not a number of points."));
                continue;
            }

            var player = match.Groups["player"].Value.Trim();
            lines.Add(new StandingsImportLine(lineNumber, player, UserIdOf(player), points));
        }

        return new StandingsImportParse(lines, errors);
    }

    /// <summary>The GeoGuessr user id a player reference names directly, or null for a nickname.</summary>
    public static string? UserIdOf(string player)
    {
        var link = ProfileLinkRegex().Match(player);
        if (link.Success)
        {
            return link.Groups[1].Value.ToLowerInvariant();
        }

        return UserIdRegex().IsMatch(player) ? player.ToLowerInvariant() : null;
    }

    // [rank.] player (separator | whitespace) points [pts]. A hyphen only separates when a space follows
    // it: "Bert -3" is a negative number, which is refused rather than read as 3.
    [GeneratedRegex(@"^(?:\d+[.)]\s+)?(?<player>.+?)(?:\s*[:–—|=]\s*|\s*-\s+|\s+)(?<points>\d+)\s*(?:points?|pts?|p)?\.?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex LineRegex();

    // Also matches localised profile links such as geoguessr.com/de/user/<id>.
    [GeneratedRegex(@"geoguessr\.com/(?:[a-z]{2}(?:-[a-z]{2})?/)?user/([0-9a-f]{24})", RegexOptions.IgnoreCase)]
    private static partial Regex ProfileLinkRegex();

    [GeneratedRegex(@"^[0-9a-f]{24}$", RegexOptions.IgnoreCase)]
    private static partial Regex UserIdRegex();
}
