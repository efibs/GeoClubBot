using Discord;
using Entities;

namespace GeoClubBot.Discord.InputAdapters.Interactions.Activity;

internal static class ActivityProgressFormatter
{
    private static readonly Color ActivityColor = new(0x1A, 0xBC, 0x9C);

    public static EmbedBuilder BuildActivityEmbed(ClubMemberWeekActivity activity, string title) =>
        new EmbedBuilder()
            .WithTitle(title)
            .WithColor(ActivityColor)
            .AddField("🏆 XP Earned", $"**{activity.TotalXp:N0} XP**", inline: true)
            .AddField("📆 Days Completed", $"**{activity.NumDaysDone} / {activity.DailyMissions.Count}**", inline: true)
            .AddField("Progress", BuildProgressValue(activity.DailyMissions));

    public static string BuildProgressValue(IReadOnlyList<DayMissionStatus> missions)
    {
        if (missions.Count == 0)
            return "No days tracked yet";

        // Weekday letters (Mo Tu We …) read cleanly for up to a week; beyond that they repeat and
        // become ambiguous, so switch to day-of-month numbers.
        var labelRow = missions.Count <= 7
            ? string.Join(" ", missions.Select(d => WeekdayLabel(d.Date)))
            : string.Join(" ", missions.Select(d => d.Date.Day.ToString("D2")));
        var emojiRow = string.Join(" ", missions.Select(d => d.MissionCompleted ? "🟩" : "⬛"));

        return $"`{labelRow}`\n{emojiRow}";
    }

    private static string WeekdayLabel(DateOnly date) => date.DayOfWeek switch
    {
        DayOfWeek.Monday    => "Mo",
        DayOfWeek.Tuesday   => "Tu",
        DayOfWeek.Wednesday => "We",
        DayOfWeek.Thursday  => "Th",
        DayOfWeek.Friday    => "Fr",
        DayOfWeek.Saturday  => "Sa",
        _                   => "Su"
    };
}
