using System.Globalization;
using System.Text;
using Discord;
using Entities;

namespace GeoClubBot.Discord.InputAdapters.Interactions.Activity;

internal static class ActivityProgressFormatter
{
    private static readonly Color ActivityColor = new(0x1A, 0xBC, 0x9C);
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private const string Legend = "🟩 streak kept · ⬛ not kept · digits: club missions finished that day";

    private static readonly string[] Keycaps = ["➖", "1️⃣", "2️⃣", "3️⃣", "4️⃣", "5️⃣", "6️⃣", "7️⃣", "8️⃣", "9️⃣"];

    /// <param name="perfectMessage">Shown when the member kept the streak every day (or met every requirement).</param>
    /// <param name="joinedNote">Shown above the legend when the member joined during the period.</param>
    public static EmbedBuilder BuildActivityEmbed(
        ClubMemberActivitySummary activity,
        string title,
        string? perfectMessage = null,
        string? joinedNote = null)
    {
        var xp = activity.RuleXp is { } ruleXp && ruleXp != activity.TotalXp
            ? $"**{activity.TotalXp.ToString("N0", Invariant)} XP** ({ruleXp.ToString("N0", Invariant)} rule XP)"
            : $"**{activity.TotalXp.ToString("N0", Invariant)} XP**";

        var missions = activity.BoardClearBonusXp > 0
            ? $"**{activity.BoardMissions}** (+{activity.BoardClearBonusXp.ToString(Invariant)} XP board bonus)"
            : $"**{activity.BoardMissions}**";

        var embed = new EmbedBuilder()
            .WithTitle(title)
            .WithColor(ActivityColor)
            .AddField("🏆 XP Earned", xp, inline: true)
            .AddField("🔥 Streak", $"**{activity.NumChallengeDaysDone} / {activity.Days.Count}** days", inline: true)
            .AddField("🎯 Club Missions", missions, inline: true);

        if (activity.RequirementResults.Count > 0)
        {
            embed.AddField("📋 Requirements", BuildRequirementsValue(activity.RequirementResults));
        }

        if (BuildHelpsValue(activity) is { } helps)
        {
            embed.AddField("🤝 Helped out (unverified)", helps);
        }

        embed.AddField("Progress", BuildProgressValue(activity.Days));

        var isPerfect = activity.RequirementResults.Count > 0
            ? activity.AllRequirementsMet
            : activity.Days.Count > 0 && activity.Days.All(d => d.ChallengeDone);
        if (isPerfect && perfectMessage is not null)
        {
            embed.WithDescription(perfectMessage);
        }

        // One footer, so the legend survives a joined note instead of being replaced by it.
        embed.WithFooter(joinedNote is null ? Legend : $"{joinedNote}\n{Legend}");

        return embed;
    }

    public static string BuildRequirementsValue(IReadOnlyList<ActivityRequirementResult> requirements) =>
        string.Join(" · ", requirements.Select(r =>
            $"{(r.Met ? "✅" : "❌")} {r.Label} **{r.Actual.ToString(Invariant)}/{r.Target.ToString(Invariant)}**"));

    private static string? BuildHelpsValue(ClubMemberActivitySummary activity)
    {
        if (activity.HelpedThisWeek is null && activity.HelpedLastWeek is null)
        {
            return null;
        }

        var parts = new List<string>();
        if (activity.HelpedThisWeek is { } thisWeek)
        {
            parts.Add($"this board week **{thisWeek.ToString(Invariant)}**");
        }

        if (activity.HelpedLastWeek is { } lastWeek)
        {
            parts.Add($"last board week **{lastWeek.ToString(Invariant)}**");
        }

        return string.Join(" · ", parts);
    }

    public static string BuildProgressValue(IReadOnlyList<DayActivity> days)
    {
        if (days.Count == 0)
            return "No days tracked yet";

        // Weekday letters (Mo Tu We …) read cleanly for up to a week; beyond that they repeat and
        // become ambiguous, so switch to day-of-month numbers.
        var labelRow = days.Count <= 8
            ? string.Join(" ", days.Select(d => WeekdayLabel(d.Date)))
            : string.Join(" ", days.Select(d => d.Date.Day.ToString("D2", Invariant)));

        var builder = new StringBuilder()
            .Append('`').Append(labelRow).Append('`').Append('\n')
            .Append(string.Join(" ", days.Select(d => d.ChallengeDone ? "🟩" : "⬛")));

        if (days.Any(d => d.BoardMissions > 0))
        {
            builder.Append('\n')
                .Append(string.Join(" ", days.Select(d => Keycaps[Math.Min(d.BoardMissions, Keycaps.Length - 1)])));
        }

        return builder.ToString();
    }

    private static string WeekdayLabel(DateOnly date) => date.DayOfWeek switch
    {
        DayOfWeek.Monday => "Mo",
        DayOfWeek.Tuesday => "Tu",
        DayOfWeek.Wednesday => "We",
        DayOfWeek.Thursday => "Th",
        DayOfWeek.Friday => "Fr",
        DayOfWeek.Saturday => "Sa",
        _ => "Su"
    };
}
