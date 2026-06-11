using System.Globalization;
using System.Text;
using Discord;
using UseCases.UseCases.DailyMissionStatistics;
// The last namespace segment shadows the equally named DTO record, so alias it back in.
using MissionStatistics = UseCases.UseCases.DailyMissionStatistics.DailyMissionStatistics;

namespace GeoClubBot.Discord.InputAdapters.Interactions.DailyMissionStatistics;

internal static class DailyMissionStatisticsFormatter
{
    private static readonly Color StatisticsColor = new(0x1A, 0xBC, 0x9C);
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // Code-block tables get unreadable past this many rows; the kinds are sorted by
    // appearance count, so the cut drops only the rarest ones.
    private const int MaxTableRows = 25;
    private const int MaxLabelLength = 26;

    /// <summary>
    /// Short, count-less English label for a mission kind, e.g. "Win Team Duels" or
    /// "Score points in Classic". Unknown types/modes fall back to the raw API values.
    /// </summary>
    public static string KindLabel(string type, string gameMode)
    {
        var modeName = _gameModeNames.GetValueOrDefault(gameMode) ?? gameMode;

        return type switch
        {
            "PlayGames" => $"Play {modeName}",
            "WinGames" => $"Win {modeName}",
            "Score" => $"Score points in {modeName}",
            _ => $"{type} {modeName}"
        };
    }

    public static EmbedBuilder BuildOverviewEmbed(MissionStatistics stats)
    {
        var description = new StringBuilder()
            .AppendLine($"**Range:** {FormatDay(stats.FromDay)} – {FormatDay(stats.ToDay)}")
            .AppendLine($"**Club:** {stats.ClubName ?? "All clubs"}")
            .AppendLine($"**Days with data:** {stats.DaysWithMissionData.ToString(Invariant)}")
            .AppendLine($"**Avg club completion:** {FormatRate(stats.AverageDayCompletionRate)}");

        if (stats.Kinds.Count == 0)
        {
            description
                .AppendLine()
                .AppendLine("No daily missions were logged in this period.");
        }
        else
        {
            description
                .AppendLine()
                .Append(BuildKindsTable(stats.Kinds));
        }

        return new EmbedBuilder()
            .WithTitle("📊 Daily Mission Statistics")
            .WithColor(StatisticsColor)
            .WithDescription(description.ToString())
            .WithFooter("Appeared = share of days with data · Done = avg club completion when present");
    }

    public static EmbedBuilder BuildDetailEmbed(MissionStatistics stats, DailyMissionKindStatistics kind)
    {
        var targetValue = kind.MinTargetProgress == kind.MaxTargetProgress
            ? FormatNumber(kind.AverageTargetProgress)
            : $"{FormatNumber(kind.AverageTargetProgress)} (between {kind.MinTargetProgress.ToString("N0", Invariant)} and {kind.MaxTargetProgress.ToString("N0", Invariant)})";

        return new EmbedBuilder()
            .WithTitle($"📊 {KindLabel(kind.Type, kind.GameMode)}")
            .WithColor(StatisticsColor)
            .WithDescription(
                $"**Range:** {FormatDay(stats.FromDay)} – {FormatDay(stats.ToDay)}\n" +
                $"**Club:** {stats.ClubName ?? "All clubs"}")
            .AddField("🔢 Appearances", kind.AppearanceCount.ToString("N0", Invariant), inline: true)
            .AddField("📆 Appeared on", $"{FormatPercent(kind.AppearanceDayShare)} of days", inline: true)
            .AddField("🕐 Last appearance", FormatLastAppearance(kind.LastAppearance), inline: true)
            .AddField("🎯 Avg target count", targetValue, inline: true)
            .AddField("✅ Club completion", FormatRate(kind.AverageDayCompletionRateWhenPresent), inline: true)
            .WithFooter("Completion = avg share of members completing the missions on days this mission appeared");
    }

    private static string BuildKindsTable(IReadOnlyList<DailyMissionKindStatistics> kinds)
    {
        var labelWidth = Math.Min(
            MaxLabelLength,
            kinds.Max(k => KindLabel(k.Type, k.GameMode).Length));
        labelWidth = Math.Max(labelWidth, "Mission".Length);

        var table = new StringBuilder()
            .AppendLine("```")
            .AppendLine($"{"Mission".PadRight(labelWidth)} │   # │ Appeared │ Avg cnt │ Done │ Last seen")
            .AppendLine(new string('─', labelWidth + 47));

        foreach (var kind in kinds.Take(MaxTableRows))
        {
            var label = Truncate(KindLabel(kind.Type, kind.GameMode), labelWidth);
            table.AppendLine(
                $"{label.PadRight(labelWidth)} │ {kind.AppearanceCount.ToString(Invariant),3} │ {FormatPercent(kind.AppearanceDayShare),8} │ {FormatNumber(kind.AverageTargetProgress),7} │ {FormatRate(kind.AverageDayCompletionRateWhenPresent),4} │ {FormatDay(kind.LastAppearance)}");
        }

        table.AppendLine("```");

        if (kinds.Count > MaxTableRows)
        {
            table.AppendLine($"…and {(kinds.Count - MaxTableRows).ToString(Invariant)} rarer mission kind(s). Use the `mission` option to inspect one.");
        }

        return table.ToString();
    }

    private static string FormatDay(DateOnly day) => day.ToString("yyyy-MM-dd", Invariant);

    private static string FormatLastAppearance(DateOnly day)
    {
        var unixSeconds = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        return $"{FormatDay(day)} (<t:{unixSeconds.ToString(Invariant)}:R>)";
    }

    private static string FormatRate(double? rate) => rate is null ? "—" : FormatPercent(rate.Value);

    private static string FormatPercent(double share) =>
        $"{Math.Round(share * 100).ToString(Invariant)}%";

    private static string FormatNumber(double value) =>
        Math.Round(value).ToString("N0", Invariant);

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : $"{value[..(maxLength - 1)]}…";

    /// <summary>
    /// Friendly English names per game mode, mirroring the table in
    /// <c>DiscordDailyMissionRenderer</c> (kept verbatim there as a faithful client port).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string?> _gameModeNames =
        new Dictionary<string, string?>
        {
            ["DailyChallenge"] = "Daily Challenge",
            ["Duels"] = "Duels",
            ["SinglePlayerQuiz"] = "Featured Quiz",
            ["ReplayableQuiz"] = "Quiz",
            ["AnyBattleRoyale"] = "Battle Royale",
            ["Classic"] = "Classic",
            ["TeamDuels"] = "Team Duels",
            ["CommunityStreak"] = "Community Streak",
            ["CountryStreak"] = "Country Streak",
            ["RankedDuels"] = "Ranked Duels",
            ["RankedTeamDuels"] = "Ranked Team Duels",
            ["UnrankedDuels"] = "Unranked Duels",
            ["UnrankedTeamDuels"] = "Unranked Team Duels",
            ["QuickPlay"] = "Quick Play",
            ["MapRunner"] = "MapRunner",
        };
}
