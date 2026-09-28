using UseCases.UseCases.CountryChallenges.Configuration;

namespace UseCases.UseCases.CountryChallenges;

/// <summary>
/// Picks the country of a challenge from its pool like drawing from a bag: every country is played once,
/// in a random order, before any comes round again, and the last country of one round never opens the
/// next. The only state is the challenge's history, so nothing has to be stored besides the posts.
/// </summary>
public static class CountryPoolRotation
{
    /// <summary>
    /// The countries the next pick may be. Walks the history oldest first, collecting countries until
    /// every country of the pool has been played, then starts a new round. Countries no longer in the pool
    /// are ignored, and one added mid-round is simply still unplayed in it.
    /// </summary>
    public static IReadOnlyList<CountryPlan> Candidates(IReadOnlyList<CountryPlan> pool, IReadOnlyList<string> history)
    {
        if (pool.Count <= 1)
        {
            return pool;
        }

        var names = pool.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var played = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? last = null;

        foreach (var country in history)
        {
            if (!names.Contains(country))
            {
                continue;
            }

            last = country;
            played.Add(country);

            if (played.Count == names.Count)
            {
                played.Clear();
            }
        }

        var candidates = pool.Where(c => !played.Contains(c.Name)).ToList();

        // Only matters at the start of a round, when every country is a candidate again.
        if (candidates.Count > 1 && last is not null)
        {
            candidates.RemoveAll(c => string.Equals(c.Name, last, StringComparison.OrdinalIgnoreCase));
        }

        return candidates;
    }

    public static CountryPlan Pick(IReadOnlyList<CountryPlan> pool, IReadOnlyList<string> history, Random random)
    {
        var candidates = Candidates(pool, history);
        return candidates[random.Next(candidates.Count)];
    }
}
