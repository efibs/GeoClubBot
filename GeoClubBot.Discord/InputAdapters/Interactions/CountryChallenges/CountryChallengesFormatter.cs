using System.Globalization;
using System.Text;
using Extensions;
using UseCases.UseCases.CountryChallenges;
using UseCases.UseCases.CountryChallenges.Rendering;

namespace GeoClubBot.Discord.InputAdapters.Interactions.CountryChallenges;

/// <summary>The replies of the country challenge commands. Every result is split to fit Discord's limit.</summary>
public static class CountryChallengesFormatter
{
    private const int DiscordMessageLimit = 2000;

    /// <summary>
    /// Reads a day the way an admin types it: a date (<c>2026-12-24</c>), or a weekday — whole or
    /// abbreviated as long as it is unambiguous (<c>sunday</c>, <c>sun</c>, <c>su</c>).
    /// </summary>
    public static bool TryParseDay(string input, out DateOnly? date, out DayOfWeek? weekday)
    {
        date = null;
        weekday = null;
        var text = input.Trim();

        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            date = parsedDate;
            return true;
        }

        if (text.Length < 2)
        {
            return false;
        }

        var matches = Enum.GetValues<DayOfWeek>()
            .Where(d => d.ToString().StartsWith(text, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count != 1)
        {
            return false;
        }

        weekday = matches[0];
        return true;
    }

    public static IReadOnlyList<string> Preview(CountryChallengePreview preview)
    {
        var summary = new StringBuilder($"## Preview of {preview.Date.DayOfWeek}, {preview.Date:yyyy-MM-dd}\n");

        if (!preview.FeatureEnabled)
        {
            summary.AppendLine(":pause_button: The country challenges are switched off (`CountryChallenges:Enabled`); nothing is posted until they are switched on.");
        }

        foreach (var warning in preview.Warnings)
        {
            summary.AppendLine($":warning: {warning}");
        }

        foreach (var note in preview.Notes)
        {
            summary.AppendLine($"- {note}");
        }

        summary.AppendLine(preview.Messages.Count == 0
            ? "Nothing would be posted."
            : "Nothing was posted. The messages below are what a run would post; challenge links are placeholders.");

        var parts = new List<string> { summary.ToString() };
        parts.AddRange(preview.Messages.Select(m => $"**{m.Title}** → <#{m.ChannelId}>\n{m.Content}"));

        return Split(parts);
    }

    public static IReadOnlyList<string> RunReport(CountryChallengeRunReport report)
    {
        var builder = new StringBuilder($"## Country challenges of {report.Date.DayOfWeek}, {report.Date:yyyy-MM-dd}\n");

        builder.AppendLine(report.Evaluation switch
        {
            null => ":x: Evaluating the results failed; see the logs.",
            { Evaluated.Count: 0, Failed.Count: 0 } => "- No results were due.",
            { Evaluated.Count: > 0 } evaluation => $"- Results evaluated: {string.Join(", ", evaluation.Evaluated)}.",
            _ => "- No results could be evaluated."
        });

        if (report.Evaluation?.Failed is { Count: > 0 } unevaluated)
        {
            builder.AppendLine($":warning: The highscores of {string.Join(", ", unevaluated)} could not be read; they are retried at the next run.");
        }

        builder.AppendLine(report.Leaderboard?.Status switch
        {
            null => ":x: Posting the leaderboard failed; see the logs.",
            CountryChallengeLeaderboardStatus.Posted => "- Leaderboard posted.",
            CountryChallengeLeaderboardStatus.AlreadyPosted => "- The leaderboard was already posted today.",
            CountryChallengeLeaderboardStatus.Empty => "- The leaderboard is due, but nobody has points yet.",
            CountryChallengeLeaderboardStatus.Failed => ":x: The leaderboard could not be posted; see the logs.",
            _ => "- The leaderboard is not due today."
        });

        var announcement = report.Announcement;
        if (announcement is null)
        {
            builder.AppendLine(":x: Announcing today's challenges failed; see the logs.");
        }
        else
        {
            if (announcement.Announced.Count > 0)
            {
                builder.AppendLine($"- Announced: {string.Join(", ", announcement.Announced)}.");
            }

            if (announcement.AlreadyPosted.Count > 0)
            {
                builder.AppendLine($"- Already posted today: {string.Join(", ", announcement.AlreadyPosted)}.");
            }

            if (announcement.Failed.Count > 0)
            {
                builder.AppendLine($":warning: Not announced: {string.Join(", ", announcement.Failed)}. Run this command again to retry.");
            }

            if (announcement is { Announced.Count: 0, AlreadyPosted.Count: 0, Failed.Count: 0 })
            {
                builder.AppendLine("- No challenge is scheduled for today.");
            }
        }

        foreach (var warning in report.Warnings)
        {
            builder.AppendLine($":warning: {warning}");
        }

        return Split([builder.ToString()]);
    }

    public static IReadOnlyList<string> EvaluationReport(CountryChallengeEvaluationOutcome outcome)
    {
        if (outcome is { Evaluated.Count: 0, Failed.Count: 0 })
        {
            return ["No country challenge is waiting for its results."];
        }

        var builder = new StringBuilder("## Country challenge results\n");

        if (outcome.Evaluated.Count > 0)
        {
            builder.AppendLine($"- Results evaluated: {string.Join(", ", outcome.Evaluated)}.");
            builder.AppendLine("- Their points count towards the leaderboard from now on; it is posted on its next day.");
        }

        if (outcome.Failed.Count > 0)
        {
            builder.AppendLine($":warning: The highscores of {string.Join(", ", outcome.Failed)} could not be read; they stay pending.");
        }

        return Split([builder.ToString()]);
    }

    public static IReadOnlyList<string> Leaderboard(CountryChallengeLeaderboard leaderboard)
    {
        var builder = new StringBuilder($"## :bar_chart: Country challenge leaderboard · {leaderboard.Season}\n");

        if (leaderboard.Top.Count == 0)
        {
            builder.AppendLine("Nobody has points yet.");
        }

        foreach (var standing in leaderboard.Top)
        {
            var line = CountryChallengeMessages.LeaderboardLine(standing);
            builder.AppendLine(standing.UserId == leaderboard.Viewer?.UserId ? $"**{line}**" : line);
        }

        builder.AppendLine();
        builder.AppendLine(leaderboard switch
        {
            { Viewer: { } viewer } => $"You: #{viewer.Rank} with {viewer.Points} {(viewer.Points == 1 ? "point" : "points")}.",
            { ViewerLinked: true } => "You have no points yet.",
            _ => "Link your GeoGuessr account with `/gg-account link` to see your own place."
        });

        return Split([builder.ToString()]);
    }

    public static IReadOnlyList<string> Import(CountryChallengeStandingsImport import)
    {
        var builder = new StringBuilder(
            $":white_check_mark: Imported {import.PlayerCount} player(s) with {import.TotalPoints} points into **{import.Season}**. " +
            "Importing again replaces this import.\n");

        builder.AppendLine("### The leaderboard now");
        foreach (var standing in import.Top)
        {
            builder.AppendLine(CountryChallengeMessages.LeaderboardLine(standing));
        }

        return Split([builder.ToString()]);
    }

    public static IReadOnlyList<string> Split(IEnumerable<string> parts) =>
        [.. parts.SelectMany(p => p.Trim().SplitAtCharWithLimit("\n", DiscordMessageLimit)).Where(p => p.Length > 0)];
}
