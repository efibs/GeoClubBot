using Entities;
using FluentAssertions;
using GeoClubBot.Discord.OutputAdapters;
using Xunit;

namespace GeoClubBot.Tests.Discord;

public sealed class DiscordActivityStatusMessageFormatterTests
{
    private readonly DiscordActivityStatusMessageFormatter _formatter = new();

    [Fact]
    public void FormatStatusUpdateHeader_ShowsNoneIndicator_WhenChunkIsEmpty()
    {
        var output = _formatter.FormatStatusUpdateHeader([], "TestClub", minXP: 100);

        output.Should().Contain("======= Activity status update - TestClub =======");
        output.Should().Contain("100XP requirement");
        output.Should().Contain("None :)");
    }

    [Fact]
    public void FormatPlayerChunk_RendersRegularStrikeAndOutOfStrikesPlayers()
    {
        var players = new List<ClubMemberActivityStatus>
        {
            // Regular strike — bullet line
            new("Alice", "user-1", TargetAchieved: false, XpSinceLastUpdate: 40,
                NumStrikes: 2, IsOutOfStrikes: false, IndividualTarget: 100, IndividualTargetReason: null),
            // Out-of-strikes — red ANSI code block, marked for kick
            new("Bob", "user-2", TargetAchieved: false, XpSinceLastUpdate: 10,
                NumStrikes: 4, IsOutOfStrikes: true, IndividualTarget: 100, IndividualTargetReason: null),
        };

        var output = _formatter.FormatPlayerChunk(players);

        output.Should().Contain("* Alice got only 40XP");
        output.Should().Contain("is now on 2 strikes.");
        output.Should().Contain("\e[2;31mBob\e[0m");
        output.Should().Contain("needs to be kicked");
        output.Should().Contain("already had 3 strikes");
    }

    [Fact]
    public void FormatPlayerChunk_IncludesIndividualTargetClause_WhenReasonPresent()
    {
        var players = new List<ClubMemberActivityStatus>
        {
            new("Carol", "user-3", TargetAchieved: false, XpSinceLastUpdate: 20,
                NumStrikes: 1, IsOutOfStrikes: false, IndividualTarget: 50,
                IndividualTargetReason: "Excused"),
        };

        var output = _formatter.FormatPlayerChunk(players);

        output.Should().Contain("(individual target: 50XP - Excused)");
    }

    [Fact]
    public void FormatIndividualTargets_ListsEachPlayerWithReason()
    {
        var players = new List<ClubMemberActivityStatus>
        {
            new("Dave", "user-4", TargetAchieved: true, XpSinceLastUpdate: 90,
                NumStrikes: 0, IsOutOfStrikes: false, IndividualTarget: 75,
                IndividualTargetReason: "New member"),
        };

        var output = _formatter.FormatIndividualTargets(players);

        output.Should().Contain("Players that have an individual target:");
        output.Should().Contain("* Dave - individual target: 75XP; Reason(s): New member");
    }

    [Fact]
    public void FormatAverageXpSummary_RendersTopAndBottom_AndReturnsNullWhenEmpty()
    {
        _formatter.FormatAverageXpSummary([], [], historyDepth: 4).Should().BeNull();

        var top = new List<ClubMemberAverageXp>
        {
            new("Eve", AverageXp: 220.5, JoinedAt: DateTimeOffset.UtcNow.AddMonths(-1)),
        };
        var bottom = new List<ClubMemberAverageXp>
        {
            new("Frank", AverageXp: 30.0, JoinedAt: DateTimeOffset.UtcNow.AddMonths(-2)),
        };

        var output = _formatter.FormatAverageXpSummary(top, bottom, historyDepth: 4);

        output.Should().NotBeNull();
        output!.Should().Contain("Top 1 members by average XP (last 4 intervals):");
        output.Should().Contain("1. Eve — 220.5XP");
        output.Should().Contain("Bottom 1 members by average XP (last 4 intervals):");
        output.Should().Contain("1. Frank — 30.0XP");
    }
}
