namespace UseCases.UseCases.RandomDraws;

/// <summary>
/// The draws behind <c>/random</c>, pure apart from the <see cref="Random"/> they are given, so a seeded
/// one makes them repeatable in tests. Every draw from a pool is without repeats.
/// </summary>
public static class RandomDraw
{
    /// <summary>
    /// <paramref name="count"/> distinct items of the pool in the order drawn (a partial Fisher–Yates
    /// shuffle), or all of them shuffled when the pool is smaller.
    /// </summary>
    public static List<T> Pick<T>(IReadOnlyList<T> pool, int count, Random random)
    {
        var items = pool.ToArray();
        var take = Math.Clamp(count, 0, items.Length);

        for (var i = 0; i < take; i++)
        {
            var j = random.Next(i, items.Length);
            (items[i], items[j]) = (items[j], items[i]);
        }

        return items.Take(take).ToList();
    }

    /// <summary>The whole pool in a random order.</summary>
    public static List<T> Shuffle<T>(IReadOnlyList<T> pool, Random random) => Pick(pool, pool.Count, random);

    /// <summary>
    /// The pool split into <paramref name="teamCount"/> teams whose sizes differ by at most one: the pool
    /// is shuffled and dealt out like cards, so which team gets an extra member is random too.
    /// </summary>
    public static List<List<T>> Teams<T>(IReadOnlyList<T> pool, int teamCount, Random random)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(teamCount, 1);

        var teams = Enumerable.Range(0, teamCount).Select(_ => new List<T>()).ToList();
        var order = Shuffle(Enumerable.Range(0, teamCount).ToList(), random);
        var shuffled = Shuffle(pool, random);

        for (var i = 0; i < shuffled.Count; i++)
        {
            teams[order[i % teamCount]].Add(shuffled[i]);
        }

        return teams;
    }

    /// <summary>
    /// <paramref name="count"/> whole numbers from <paramref name="min"/> to <paramref name="max"/>
    /// (both included). Without repeats they are distinct, which needs a range at least that large.
    /// </summary>
    public static List<long> Numbers(long min, long max, int count, bool allowRepeats, Random random)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(min, max);

        var rangeSize = max - min + 1;
        if (!allowRepeats && rangeSize < count)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "The range holds fewer distinct numbers.");
        }

        var drawn = new List<long>(count);
        var seen = new HashSet<long>();
        while (drawn.Count < count)
        {
            var number = random.NextInt64(min, max + 1);
            if (allowRepeats || seen.Add(number))
            {
                drawn.Add(number);
            }
        }

        return drawn;
    }

    /// <summary><paramref name="count"/> throws of a die with <paramref name="sides"/> sides.</summary>
    public static List<int> Dice(int sides, int count, Random random) =>
        Enumerable.Range(0, count).Select(_ => random.Next(1, sides + 1)).ToList();

    /// <summary><paramref name="count"/> coin flips; true is heads.</summary>
    public static List<bool> Coins(int count, Random random) =>
        Enumerable.Range(0, count).Select(_ => random.Next(2) == 0).ToList();

    /// <summary>
    /// The items of a list typed into one option: separated by commas, semicolons or line breaks,
    /// trimmed, blanks dropped. Repeats are kept, so an item typed twice is twice as likely.
    /// </summary>
    public static List<string> ParseList(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split([',', ';', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
}
