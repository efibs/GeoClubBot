using CsCheck;
using Entities;
using FluentAssertions;
using UseCases.UseCases.CountryChallenges;
using Xunit;

namespace GeoClubBot.Tests.PropertyBased;

/// <summary>
/// Invariants of the leaderboard over arbitrary awards: every point is counted exactly once, and a rank
/// is always one more than the number of players strictly ahead — which is what makes ties share a rank.
/// </summary>
public sealed class LeaderboardRankingPropertyTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly Gen<List<CountryChallengePointAward>> GenAwards =
        Gen.Select(Gen.Int[0, 6], Gen.Int[0, 12], Gen.Int[0, 1000], (player, points, minutes) =>
                CountryChallengePointAward.Imported("S", $"user-{player}", $"Player {player}", points, Start.AddMinutes(minutes)))
            .List[0, 40];

    [Fact]
    public void Counts_every_point_exactly_once() =>
        GenAwards.Sample(awards =>
        {
            var standings = LeaderboardRanking.Rank(awards);

            standings.Sum(s => s.Points).Should().Be(awards.Sum(a => a.Points));
            standings.Select(s => s.UserId).Should().OnlyHaveUniqueItems();
            standings.Should().OnlyContain(s => s.Points > 0);
        });

    [Fact]
    public void A_rank_is_one_more_than_the_players_strictly_ahead() =>
        GenAwards.Sample(awards =>
        {
            var standings = LeaderboardRanking.Rank(awards);

            foreach (var standing in standings)
            {
                standing.Rank.Should().Be(1 + standings.Count(s => s.Points > standing.Points));
            }

            standings.Select(s => s.Points).Should().BeInDescendingOrder();
        });
}
