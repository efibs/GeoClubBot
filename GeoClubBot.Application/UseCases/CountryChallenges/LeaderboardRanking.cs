using Entities;

namespace UseCases.UseCases.CountryChallenges;

/// <summary>One player's line on the country challenge leaderboard.</summary>
public sealed record CountryChallengeStanding(int Rank, string UserId, string Nickname, int Points);

/// <summary>Turns point awards into the leaderboard.</summary>
public static class LeaderboardRanking
{
    /// <summary>
    /// One line per player with points: the sum of their awards, shown under the nickname of their most
    /// recent award. Ranking is competition style — tied players share a rank and the next rank is
    /// skipped (1, 2, 2, 4) — so a rank is always one more than the number of players strictly ahead.
    /// </summary>
    public static IReadOnlyList<CountryChallengeStanding> Rank(IEnumerable<CountryChallengePointAward> awards)
    {
        var totals = awards
            .GroupBy(a => a.UserId, StringComparer.Ordinal)
            .Select(g =>
            {
                var latest = g.OrderByDescending(a => a.AwardedAt).ThenByDescending(a => a.Id).First();
                return (UserId: g.Key, latest.Nickname, Points: g.Sum(a => a.Points));
            })
            .Where(t => t.Points > 0)
            .OrderByDescending(t => t.Points)
            .ThenBy(t => t.Nickname, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.UserId, StringComparer.Ordinal)
            .ToList();

        var standings = new List<CountryChallengeStanding>(totals.Count);
        for (var i = 0; i < totals.Count; i++)
        {
            var rank = i > 0 && totals[i].Points == totals[i - 1].Points ? standings[i - 1].Rank : i + 1;
            standings.Add(new CountryChallengeStanding(rank, totals[i].UserId, totals[i].Nickname, totals[i].Points));
        }

        return standings;
    }
}
