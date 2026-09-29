using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using Xunit;
using static GeoClubBot.Tests.TestBuilders.MissionBoards;

namespace GeoClubBot.Tests.Domain;

public sealed class ClubMissionBoardWeekTests
{
    private static readonly DateTimeOffset Reset = new(2026, 9, 29, 11, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset NoFallback(DateTimeOffset _) =>
        throw new InvalidOperationException("The board reported its reset; the fallback must not be used.");

    [Fact]
    public void ClaimCycleStart_IsOneDayBeforeTheReportedNextReset()
    {
        var week = Week(Board(1, FreeTiles(9)), nextClaimResetAt: Reset);

        week.ClaimCycleStart(Reset.AddHours(-3), NoFallback).Should().Be(Reset.AddDays(-1));
    }

    [Fact]
    public void ClaimCycleStart_StepsForward_WhenTheReportedResetHasPassed()
    {
        // A board cached just before the reset still reports that reset as "next".
        var week = Week(Board(1, FreeTiles(9)), nextClaimResetAt: Reset);

        week.ClaimCycleStart(Reset.AddHours(2), NoFallback).Should().Be(Reset);
        week.ClaimCycleStart(Reset.AddDays(1).AddHours(2), NoFallback).Should().Be(Reset.AddDays(1));
    }

    [Fact]
    public void ClaimCycleStart_UsesTheFallback_WhenTheBoardDoesNotSay()
    {
        var week = Week(Board(1, FreeTiles(9))) with { NextClaimResetAt = null };
        var fallback = new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

        week.ClaimCycleStart(fallback.AddHours(1), _ => fallback).Should().Be(fallback);
    }

    [Fact]
    public void ClaimStateOf_ClaimAvailable_WhenAMissionIsFreeAndTheMemberHasNotClaimedToday()
    {
        var week = Week(Board(1, FreeTiles(9)));

        week.ClaimStateOf("u1", Reset.AddDays(-1)).Should().Be(ClubMissionClaimState.ClaimAvailable);
    }

    [Fact]
    public void ClaimStateOf_HoldingOpenMission_WinsOverEverythingElse()
    {
        var tiles = FreeTiles(9);
        tiles[0] = Tile(claimedBy: "u1", claimedAt: Reset.AddDays(-3));
        var week = Week(Board(1, tiles));

        week.ClaimStateOf("u1", Reset.AddDays(-1)).Should().Be(ClubMissionClaimState.HoldingOpenMission);
        week.OpenClaimOf("u1").Should().Be(tiles[0]);
    }

    [Fact]
    public void ClaimStateOf_ClaimedThisCycle_WhenTheMemberClaimedSinceTheReset()
    {
        var tiles = FreeTiles(9);
        tiles[0] = Tile(claimedBy: "u1", claimedAt: Reset.AddHours(-2), completed: true);
        var week = Week(Board(1, tiles));

        week.ClaimStateOf("u1", Reset.AddDays(-1)).Should().Be(ClubMissionClaimState.ClaimedThisCycle);
    }

    [Fact]
    public void ClaimStateOf_ClaimAvailable_WhenTheLastClaimWasInAnEarlierCycle()
    {
        var tiles = FreeTiles(9);
        tiles[0] = Tile(claimedBy: "u1", claimedAt: Reset.AddDays(-2), completed: true);
        var week = Week(Board(1, tiles));

        week.ClaimStateOf("u1", Reset.AddDays(-1)).Should().Be(ClubMissionClaimState.ClaimAvailable);
    }

    [Fact]
    public void ClaimStateOf_NoneFree_WhenTheCurrentBoardIsFullyClaimed()
    {
        var tiles = Enumerable.Range(0, 9).Select(i => Tile(index: i, claimedBy: $"other{i}")).ToArray();
        var week = Week(Board(1, tiles));

        week.FreeTiles.Should().BeEmpty();
        week.ClaimStateOf("u1", Reset.AddDays(-1)).Should().Be(ClubMissionClaimState.NoneFree);
    }

    [Fact]
    public void ClaimStateOf_NoneFree_OnceEveryBoardIsCleared()
    {
        var tiles = Enumerable.Range(0, 9).Select(i => Tile(index: i, claimedBy: $"other{i}", completed: true)).ToArray();
        var week = Week(Board(1, tiles), allBoardsCleared: true);

        week.CurrentBoard.Should().BeNull();
        week.ClaimStateOf("u1", Reset.AddDays(-1)).Should().Be(ClubMissionClaimState.NoneFree);
    }

    [Fact]
    public void HelpedBy_CountsOthersMissions_NotTheMembersOwn()
    {
        var tiles = FreeTiles(9);
        tiles[0] = Tile(claimedBy: "other", helpers: ["u1"]);
        tiles[1] = Tile(claimedBy: "other2", helpers: ["u1", "u2"], completed: true);
        tiles[2] = Tile(claimedBy: "u1", helpers: ["u1"]);
        var week = Week(Board(1, tiles));

        week.HelpedBy("u1").Should().HaveCount(2);
        week.HelpedBy("u2").Should().ContainSingle();
    }

    [Fact]
    public void Board_RemainingCount_CountsClaimedAndFreeMissionsNotYetCompleted()
    {
        var tiles = FreeTiles(9);
        tiles[0] = Tile(claimedBy: "a", completed: true);
        tiles[1] = Tile(claimedBy: "b");
        var board = Board(1, tiles);

        board.CompletedCount.Should().Be(1);
        board.RemainingCount.Should().Be(8);
        board.IsCleared.Should().BeFalse();
    }
}
