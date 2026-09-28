namespace UseCases.UseCases.CountryChallenges;

/// <summary>Roles to hand out: <c>RoleIdsByPlace[i]</c> goes to every GeoGuessr player in <c>PlayersByPlace[i]</c>.</summary>
public sealed record RoleAssignment(IReadOnlyList<ulong> RoleIdsByPlace, IReadOnlyList<IReadOnlyList<string>> PlayersByPlace);

/// <summary>Decides who gets which country challenge role.</summary>
public static class CountryChallengeRoleAllocation
{
    /// <summary>
    /// Challenges evaluated together that use the same roles share them: the roles are taken from their
    /// previous holders once, and each player keeps only the best place they reached in any of those
    /// challenges. Without the grouping, the second challenge of a Friday would take the roles the first
    /// had just handed out.
    /// </summary>
    public static IReadOnlyList<RoleAssignment> ForResults(
        IEnumerable<(IReadOnlyList<ulong> RoleIds, IReadOnlyList<string> PlayersInPlaceOrder)> results)
    {
        return results
            .Where(r => r.RoleIds.Count > 0)
            .GroupBy(r => string.Join(',', r.RoleIds))
            .Select(group =>
            {
                var roleIds = group.First().RoleIds;
                var bestPlace = new Dictionary<string, int>(StringComparer.Ordinal);

                foreach (var (_, players) in group)
                {
                    for (var place = 0; place < players.Count && place < roleIds.Count; place++)
                    {
                        if (!bestPlace.TryGetValue(players[place], out var best) || place < best)
                        {
                            bestPlace[players[place]] = place;
                        }
                    }
                }

                return ByPlace(roleIds, bestPlace);
            })
            .ToList();
    }

    /// <summary>
    /// Leaderboard roles go by rank, so players tied on a rank share its role and the rank they push out
    /// gets none — the same way the ranks themselves are counted.
    /// </summary>
    public static RoleAssignment? ForLeaderboard(IReadOnlyList<ulong> roleIds, IReadOnlyList<CountryChallengeStanding> standings)
    {
        if (roleIds.Count == 0)
        {
            return null;
        }

        var places = standings
            .Where(s => s.Rank <= roleIds.Count)
            .ToDictionary(s => s.UserId, s => s.Rank - 1, StringComparer.Ordinal);

        return ByPlace(roleIds, places);
    }

    private static RoleAssignment ByPlace(IReadOnlyList<ulong> roleIds, Dictionary<string, int> places)
    {
        var playersByPlace = Enumerable.Range(0, roleIds.Count)
            .Select(place => (IReadOnlyList<string>)places.Where(p => p.Value == place).Select(p => p.Key).Order(StringComparer.Ordinal).ToList())
            .ToList();

        return new RoleAssignment(roleIds, playersByPlace);
    }
}
