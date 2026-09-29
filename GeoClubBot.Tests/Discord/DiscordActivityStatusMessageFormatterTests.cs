using Entities;
using FluentAssertions;
using GeoClubBot.Discord.OutputAdapters;
using Xunit;
using static VerifyXunit.Verifier;

namespace GeoClubBot.Tests.Discord;

/// <summary>
/// The formatter produces multi-line Discord messages (Markdown + ANSI code blocks). Asserting
/// the whole rendered block via Verify snapshots — rather than a handful of <c>Contain</c> checks —
/// makes the exact output reviewable, so any wording/spacing/colour change shows up as a diff.
/// The committed expectations live in the <c>*.verified.txt</c> files beside this one.
/// </summary>
public sealed class DiscordActivityStatusMessageFormatterTests
{
    private readonly DiscordActivityStatusMessageFormatter _formatter = new();

    private static ActivityRequirementResult Streak(int actual, int target = 6) =>
        new(ClubXpActivityKind.DailyChallengeOrDuel, actual, target, 6);

    private static ActivityRequirementResult Missions(int actual, int target = 2) =>
        new(ClubXpActivityKind.BoardMission, actual, target, 2);

    [Fact]
    public Task FormatStatusUpdateHeader_WithNoPlayers_ShowsNoneIndicator() =>
        Verify(_formatter.FormatStatusUpdateHeader([], "TestClub", requirements: "streak 6 · missions 2"));

    [Fact]
    public Task FormatPlayerChunk_RegularStrikeAndOutOfStrikesPlayers()
    {
        var players = new List<ClubMemberActivityStatus>
        {
            // Regular strike — bullet line naming what was missed.
            new("Alice", "user-1", TargetAchieved: false, XpSinceLastUpdate: 140,
                NumStrikes: 2, IsOutOfStrikes: false, IndividualTarget: 0, IndividualTargetReason: null,
                RuleXp: 120, Requirements: [Streak(5), Missions(1)]),
            // Out-of-strikes — red ANSI code block, marked for kick.
            new("Bob", "user-2", TargetAchieved: false, XpSinceLastUpdate: 20,
                NumStrikes: 4, IsOutOfStrikes: true, IndividualTarget: 0, IndividualTargetReason: null,
                RuleXp: 20, Requirements: [Streak(1), Missions(0)]),
        };

        return Verify(_formatter.FormatPlayerChunk(players));
    }

    [Fact]
    public Task FormatPlayerChunk_IncludesIndividualTargetClause_WhenReasonPresent()
    {
        var players = new List<ClubMemberActivityStatus>
        {
            new("Carol", "user-3", TargetAchieved: false, XpSinceLastUpdate: 40,
                NumStrikes: 1, IsOutOfStrikes: false, IndividualTarget: 0,
                IndividualTargetReason: "Excused", RuleXp: 40, Requirements: [Streak(2, target: 3), Missions(1, target: 1)]),
        };

        return Verify(_formatter.FormatPlayerChunk(players));
    }

    [Fact]
    public Task FormatPlayerChunk_FallsBackToXp_WhenNoRequirementsWereEvaluated()
    {
        var players = new List<ClubMemberActivityStatus>
        {
            new("Erin", "user-5", TargetAchieved: false, XpSinceLastUpdate: 30,
                NumStrikes: 1, IsOutOfStrikes: false, IndividualTarget: 100, IndividualTargetReason: null),
        };

        return Verify(_formatter.FormatPlayerChunk(players));
    }

    [Fact]
    public Task FormatIndividualTargets_ListsEachPlayerWithReason()
    {
        var players = new List<ClubMemberActivityStatus>
        {
            new("Dave", "user-4", TargetAchieved: true, XpSinceLastUpdate: 90,
                NumStrikes: 0, IsOutOfStrikes: false, IndividualTarget: 0,
                IndividualTargetReason: "New member", RuleXp: 90, Requirements: [Streak(4, target: 4), Missions(1, target: 1)]),
        };

        return Verify(_formatter.FormatIndividualTargets(players));
    }

    [Fact]
    public Task FormatAverageXpSummary_RendersTopAndBottom()
    {
        var top = new List<ClubMemberAverageXp>
        {
            new("Eve", AverageXp: 220.5, JoinedAt: DateTimeOffset.UnixEpoch),
        };
        var bottom = new List<ClubMemberAverageXp>
        {
            new("Frank", AverageXp: 30.0, JoinedAt: DateTimeOffset.UnixEpoch),
        };

        return Verify(_formatter.FormatAverageXpSummary(top, bottom, historyDepth: 4));
    }

    [Fact]
    public void FormatAverageXpSummary_ReturnsNull_WhenNoMembers() =>
        _formatter.FormatAverageXpSummary([], [], historyDepth: 4).Should().BeNull();
}
