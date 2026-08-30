using System.Text;
using Discord;
using FluentAssertions;
using GeoClubBot.Discord.InputAdapters.Interactions.DailyMissionStatistics;
using UseCases.UseCases.DailyMissionStatistics;
using Xunit;
using static VerifyXunit.Verifier;
using MissionStatistics = UseCases.UseCases.DailyMissionStatistics.DailyMissionStatistics;

namespace GeoClubBot.Tests.Discord;

/// <summary>
/// The statistics embeds contain a hand-aligned code-block table, so the whole rendered embed is
/// snapshot-tested via Verify — any spacing/wording change shows up as a reviewable diff in the
/// committed <c>*.verified.txt</c> files beside this test.
/// </summary>
public sealed class DailyMissionStatisticsFormatterTests
{
    // Fixed dates keep the snapshots (including the <t:...:R> Discord timestamp) deterministic.
    private static readonly DateOnly FromDay = new(2026, 5, 11);
    private static readonly DateOnly ToDay = new(2026, 6, 9);

    private static MissionStatistics BuildStats(
        string? clubName = null,
        params DailyMissionKindStatistics[] kinds) =>
        new(
            ClubName: clubName,
            FromDay: FromDay,
            ToDay: ToDay,
            DaysWithMissionData: 28,
            TotalMissionAppearances: kinds.Sum(k => k.AppearanceCount),
            AverageDayCompletionRate: kinds.Length == 0 ? null : 0.62,
            Kinds: kinds);

    private static DailyMissionKindStatistics BuildKind(
        string type,
        string gameMode,
        int appearances,
        double dayShare,
        double avgTarget,
        int minTarget,
        int maxTarget,
        DateOnly lastAppearance,
        double? completionRate) =>
        new(type, gameMode, appearances, dayShare, avgTarget, minTarget, maxTarget, lastAppearance, completionRate);

    [Fact]
    public Task BuildOverviewEmbed_WithSeveralKinds_RendersTheTable()
    {
        var stats = BuildStats(
            clubName: null,
            BuildKind("WinGames", "Duels", 12, 12.0 / 28.0, 3.4, 1, 5, new DateOnly(2026, 6, 8), 0.71),
            BuildKind("Score", "Classic", 9, 9.0 / 28.0, 21666.7, 15000, 25000, new DateOnly(2026, 6, 9), 0.55),
            BuildKind("PlayGames", "DailyChallenge", 7, 7.0 / 28.0, 1.0, 1, 1, new DateOnly(2026, 5, 30), null));

        return Verify(RenderEmbed(DailyMissionStatisticsFormatter.BuildOverviewEmbed(stats).Build()));
    }

    [Fact]
    public Task BuildOverviewEmbed_WithoutMissions_ExplainsTheEmptyResult()
    {
        var stats = BuildStats(clubName: "My Club") with { DaysWithMissionData = 0 };

        return Verify(RenderEmbed(DailyMissionStatisticsFormatter.BuildOverviewEmbed(stats).Build()));
    }

    [Fact]
    public Task BuildDetailEmbed_RendersAllKindStatistics()
    {
        var kind = BuildKind("WinGames", "TeamDuels", 6, 6.0 / 28.0, 3.5, 2, 5, new DateOnly(2026, 6, 7), 0.43);
        var stats = BuildStats(clubName: "My Club", kind);

        return Verify(RenderEmbed(DailyMissionStatisticsFormatter.BuildDetailEmbed(stats, kind).Build()));
    }

    [Fact]
    public Task BuildDetailEmbed_WithoutCompletionData_ShowsAPlaceholder()
    {
        var kind = BuildKind("Score", "Classic", 3, 3.0 / 28.0, 15000, 15000, 15000, new DateOnly(2026, 6, 1), null);
        var stats = BuildStats(clubName: null, kind);

        return Verify(RenderEmbed(DailyMissionStatisticsFormatter.BuildDetailEmbed(stats, kind).Build()));
    }

    [Theory]
    [InlineData("WinGames", "TeamDuels", "Win Team Duels")]
    [InlineData("PlayGames", "DailyChallenge", "Play Daily Challenge")]
    [InlineData("Score", "Classic", "Score points in Classic")]
    [InlineData("Unknown", "NewMode", "Unknown NewMode")]
    public void KindLabel_BuildsFriendlyEnglishPhrases(string type, string gameMode, string expected) =>
        DailyMissionStatisticsFormatter.KindLabel(type, gameMode).Should().Be(expected);

    /// <summary>Flattens an embed into plain text so the whole layout is captured in one snapshot.</summary>
    private static string RenderEmbed(Embed embed)
    {
        var text = new StringBuilder()
            .AppendLine($"Title: {embed.Title}")
            .AppendLine("Description:")
            .AppendLine(embed.Description)
            .AppendLine();

        foreach (var field in embed.Fields)
        {
            text.AppendLine($"[{field.Name}] {field.Value}");
        }

        return text
            .AppendLine()
            .AppendLine($"Footer: {embed.Footer?.Text}")
            .ToString();
    }
}
