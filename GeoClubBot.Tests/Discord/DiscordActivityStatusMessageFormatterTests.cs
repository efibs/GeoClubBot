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

    [Fact]
    public Task FormatStatusUpdateHeader_WithNoPlayers_ShowsNoneIndicator() =>
        Verify(_formatter.FormatStatusUpdateHeader([], "TestClub", minXP: 100));

    [Fact]
    public Task FormatPlayerChunk_RegularStrikeAndOutOfStrikesPlayers()
    {
        var players = new List<ClubMemberActivityStatus>
        {
            // Regular strike — bullet line.
            new("Alice", "user-1", TargetAchieved: false, XpSinceLastUpdate: 40,
                NumStrikes: 2, IsOutOfStrikes: false, IndividualTarget: 100, IndividualTargetReason: null),
            // Out-of-strikes — red ANSI code block, marked for kick.
            new("Bob", "user-2", TargetAchieved: false, XpSinceLastUpdate: 10,
                NumStrikes: 4, IsOutOfStrikes: true, IndividualTarget: 100, IndividualTargetReason: null),
        };

        return Verify(_formatter.FormatPlayerChunk(players));
    }

    [Fact]
    public Task FormatPlayerChunk_IncludesIndividualTargetClause_WhenReasonPresent()
    {
        var players = new List<ClubMemberActivityStatus>
        {
            new("Carol", "user-3", TargetAchieved: false, XpSinceLastUpdate: 20,
                NumStrikes: 1, IsOutOfStrikes: false, IndividualTarget: 50,
                IndividualTargetReason: "Excused"),
        };

        return Verify(_formatter.FormatPlayerChunk(players));
    }

    [Fact]
    public Task FormatIndividualTargets_ListsEachPlayerWithReason()
    {
        var players = new List<ClubMemberActivityStatus>
        {
            new("Dave", "user-4", TargetAchieved: true, XpSinceLastUpdate: 90,
                NumStrikes: 0, IsOutOfStrikes: false, IndividualTarget: 75,
                IndividualTargetReason: "New member"),
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
