using Entities;
using FluentAssertions;
using UseCases.UseCases.CountryChallenges;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.CountryChallenges;

/// <summary>How points become the leaderboard, and how places become roles.</summary>
public sealed class LeaderboardAndRolesTests
{
    private static readonly DateTimeOffset Earlier = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Earlier.AddDays(7);

    // ---- Ranking ----------------------------------------------------------

    [Fact]
    public void Rank_SumsEveryAwardOfAPlayer_UnderTheirLatestNickname()
    {
        var standings = LeaderboardRanking.Rank(
        [
            CountryChallengePointAward.Imported("S1", "a", "OldName", 10, Earlier),
            CountryChallengePointAward.ForPlacement("S1", 1, 1, "a", "NewName", 3, Later),
            CountryChallengePointAward.ForPlacement("S1", 1, 2, "b", "Bert", 2, Later)
        ]);

        standings.Should().Equal(
            new CountryChallengeStanding(1, "a", "NewName", 13),
            new CountryChallengeStanding(2, "b", "Bert", 2));
    }

    [Fact]
    public void Rank_GivesTiedPlayersTheSameRank_AndSkipsTheRanksTheyShare()
    {
        var standings = LeaderboardRanking.Rank(
        [
            Award("a", "Anna", 5), Award("b", "Bert", 3), Award("c", "Cleo", 3), Award("d", "Dora", 1)
        ]);

        standings.Select(s => (s.Rank, s.Nickname)).Should().Equal((1, "Anna"), (2, "Bert"), (2, "Cleo"), (4, "Dora"));
    }

    [Fact]
    public void Rank_LeavesOutPlayersWithoutPoints()
    {
        LeaderboardRanking.Rank([Award("a", "Anna", 0)]).Should().BeEmpty();
    }

    // ---- Roles from results -----------------------------------------------

    [Fact]
    public void ForResults_SharesARoleSetBetweenChallenges_KeepingEachPlayersBestPlace()
    {
        IReadOnlyList<ulong> podium = [1, 2, 3];

        var assignments = CountryChallengeRoleAllocation.ForResults(
        [
            (podium, ["anna", "bert", "cleo"]),
            (podium, ["bert", "dora", "anna"])
        ]);

        var assignment = assignments.Should().ContainSingle("both challenges use the same roles").Subject;
        assignment.RoleIdsByPlace.Should().Equal(1UL, 2UL, 3UL);
        assignment.PlayersByPlace[0].Should().BeEquivalentTo(["anna", "bert"]);
        assignment.PlayersByPlace[1].Should().BeEquivalentTo(["dora"]);
        assignment.PlayersByPlace[2].Should().BeEquivalentTo(["cleo"]);
    }

    [Fact]
    public void ForResults_KeepsDifferentRoleSetsApart_AndSkipsChallengesWithoutRoles()
    {
        var assignments = CountryChallengeRoleAllocation.ForResults(
        [
            ([10], ["anna"]),
            ([20], ["anna"]),
            ([], ["bert"])
        ]);

        assignments.Select(a => a.RoleIdsByPlace.Single()).Should().BeEquivalentTo([10UL, 20UL]);
        assignments.Should().OnlyContain(a => a.PlayersByPlace[0].Single() == "anna");
    }

    [Fact]
    public void ForResults_GivesRolesOnlyForThePlacesThatHaveOne()
    {
        var assignment = CountryChallengeRoleAllocation.ForResults([([10], ["anna", "bert"])]).Single();

        assignment.PlayersByPlace.Should().ContainSingle().Which.Should().Equal("anna");
    }

    // ---- Roles from the leaderboard ---------------------------------------

    [Fact]
    public void ForLeaderboard_GivesTiedPlayersTheSameRole_AndNoneToTheRankTheyPushOut()
    {
        var standings = LeaderboardRanking.Rank([Award("a", "Anna", 5), Award("b", "Bert", 5), Award("c", "Cleo", 1)]);

        var assignment = CountryChallengeRoleAllocation.ForLeaderboard([1, 2, 3], standings)!;

        assignment.PlayersByPlace[0].Should().BeEquivalentTo(["a", "b"]);
        assignment.PlayersByPlace[1].Should().BeEmpty();
        assignment.PlayersByPlace[2].Should().BeEquivalentTo(["c"]);
    }

    [Fact]
    public void ForLeaderboard_IsNothing_WithoutRoles()
    {
        CountryChallengeRoleAllocation.ForLeaderboard([], [new CountryChallengeStanding(1, "a", "Anna", 5)])
            .Should().BeNull();
    }

    private static CountryChallengePointAward Award(string userId, string nickname, int points) =>
        CountryChallengePointAward.Imported("S1", userId, nickname, points, Earlier);
}
