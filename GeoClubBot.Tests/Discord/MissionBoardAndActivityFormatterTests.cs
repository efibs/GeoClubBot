using System.Text;
using Discord;
using Entities;
using GeoClubBot.Discord.InputAdapters.Interactions.Activity;
using GeoClubBot.Discord.InputAdapters.Interactions.Club;
using UseCases.UseCases.MissionBoard;
using Xunit;
using static GeoClubBot.Tests.TestBuilders.MissionBoards;
using static VerifyXunit.Verifier;

namespace GeoClubBot.Tests.Discord;

/// <summary>
/// Snapshots of the <c>/club-stats board</c> embed and the activity embeds (<c>current-week</c>,
/// <c>last-days</c>): the whole rendered layout lives in the committed <c>*.verified.txt</c> files.
/// </summary>
public sealed class MissionBoardAndActivityFormatterTests
{
    private static readonly DateTimeOffset T = PeriodStart;

    private static ClubMissionBoardView View(ClubMissionBoardWeek week) => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "Dragon",
        week,
        new Dictionary<string, string> { ["u1"] = "Alice", ["u2"] = "Bob" },
        T.AddDays(3));

    [Fact]
    public Task BoardEmbed_ShowsProgressAndOpenMissions()
    {
        var cleared = Board(1, Enumerable.Range(0, 9).Select(i => Tile(index: i, claimedBy: "x", completed: true)).ToArray());
        var current = Board(2,
        [
            Tile(boardNumber: 2, index: 0, claimedBy: "u1", claimedAt: T.AddDays(2), title: "Win 2 Ranked Duels", currentProgress: 1),
            Tile(boardNumber: 2, index: 1, claimedBy: "u2", claimedAt: T.AddDays(2).AddHours(3), helpRequestedAt: T.AddDays(3),
                title: "Do 2 5K's on World"),
            Tile(boardNumber: 2, index: 2, claimedBy: "unknown-user", claimedAt: T.AddDays(3), title: "Score 30000 points on a Classic Map"),
            .. Enumerable.Range(3, 9).Select(i => Tile(boardNumber: 2, index: i, claimedBy: "x", completed: true)),
            .. FreeTiles(4, boardNumber: 2)
        ]);
        var locked = Board(3, FreeTiles(25, boardNumber: 3));
        var week = new ClubMissionBoardWeek(T, T.AddDays(7), 2, false, [cleared, current, locked], T.AddDays(4));

        return Verify(Render(ClubMissionBoardFormatter.BuildEmbed(View(week)).Build()));
    }

    [Fact]
    public Task BoardEmbed_CelebratesAllBoardsCleared()
    {
        var boards = Enumerable.Range(1, 5)
            .Select(n => Board(n, Enumerable.Range(0, 4).Select(i => Tile(boardNumber: n, index: i, claimedBy: "x", completed: true)).ToArray()))
            .ToList();
        var week = new ClubMissionBoardWeek(T, T.AddDays(7), 6, true, boards, T.AddDays(4));

        return Verify(Render(ClubMissionBoardFormatter.BuildEmbed(View(week)).Build()));
    }

    [Fact]
    public Task ActivityEmbed_CurrentWeek_WithRequirementsAndHelps()
    {
        var days = Enumerable.Range(0, 8)
            .Select(i => new DayActivity(new DateOnly(2026, 9, 23).AddDays(i), ChallengeDone: i is not 2 and not 7, BoardMissions: i is 0 or 3 ? 1 : 0, Xp: 20))
            .ToList();
        var summary = new ClubMemberActivitySummary(
            days, TotalXp: 280, StreakDays: 6, BoardMissions: 2, BoardClearBonusXp: 100,
            JoinedInPeriod: false, JoinedDateTime: T.AddMonths(-3),
            RuleXp: 160,
            Requirements:
            [
                new ActivityRequirementResult(ClubXpActivityKind.DailyChallengeOrDuel, 6, 6, 6),
                new ActivityRequirementResult(ClubXpActivityKind.BoardMission, 2, 2, 2)
            ],
            HelpedThisWeek: 3, HelpedLastWeek: 1, PeriodStart: T);

        return Verify(Render(ActivityProgressFormatter.BuildActivityEmbed(
            summary, "📅 Your Activity This Week", "✅ All requirements met — keep it up!").Build()));
    }

    [Fact]
    public Task ActivityEmbed_LastDays_ForAMemberWhoJoinedRecently()
    {
        var days = Enumerable.Range(0, 14)
            .Select(i => new DayActivity(new DateOnly(2026, 9, 16).AddDays(i), ChallengeDone: i >= 10, BoardMissions: i == 12 ? 2 : 0, Xp: 0))
            .ToList();
        var summary = new ClubMemberActivitySummary(
            days, TotalXp: 120, StreakDays: 4, BoardMissions: 2, BoardClearBonusXp: 0,
            JoinedInPeriod: true, JoinedDateTime: new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.Zero));

        return Verify(Render(ActivityProgressFormatter.BuildActivityEmbed(
            summary, "📅 Alice's Activity — Last 14 Days", "🔥 Perfect — streak kept on all 14 days!",
            "⭐ Alice joined the club on Sep 26").Build()));
    }

    /// <summary>Flattens an embed into plain text so the whole layout is captured in one snapshot.</summary>
    private static string Render(Embed embed)
    {
        var text = new StringBuilder()
            .AppendLine($"Title: {embed.Title}")
            .AppendLine($"Description: {embed.Description}");

        foreach (var field in embed.Fields)
        {
            text.AppendLine($"[{field.Name}]{(field.Inline ? " (inline)" : string.Empty)}").AppendLine(field.Value);
        }

        return text.AppendLine($"Footer: {embed.Footer?.Text}").ToString();
    }
}
