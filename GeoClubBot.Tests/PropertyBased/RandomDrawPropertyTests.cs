using CsCheck;
using FluentAssertions;
using UseCases.UseCases.RandomDraws;
using Xunit;

namespace GeoClubBot.Tests.PropertyBased;

/// <summary>
/// A draw promises no repeats and a fair split: these check, over random pools, counts and seeds, that a
/// pick never takes an item twice nor one outside the pool, and that teams share out everyone exactly
/// once with sizes at most one apart.
/// </summary>
public sealed class RandomDrawPropertyTests
{
    private static readonly Gen<(int PoolSize, int Count, int Seed)> GenPick =
        Gen.Select(Gen.Int[0, 60], Gen.Int[0, 30], Gen.Int);

    private static readonly Gen<(int PoolSize, int Teams, int Seed)> GenTeams =
        Gen.Select(Gen.Int[0, 60], Gen.Int[1, 25], Gen.Int);

    [Fact]
    public void A_pick_takes_distinct_items_of_the_pool_and_as_many_as_it_can() =>
        GenPick.Sample(run =>
        {
            var pool = Enumerable.Range(0, run.PoolSize).ToList();

            var drawn = RandomDraw.Pick(pool, run.Count, new Random(run.Seed));

            drawn.Should().HaveCount(Math.Min(run.Count, run.PoolSize));
            drawn.Should().OnlyHaveUniqueItems().And.OnlyContain(i => i >= 0 && i < run.PoolSize);
        });

    [Fact]
    public void Teams_hold_everyone_once_and_differ_by_at_most_one() =>
        GenTeams.Sample(run =>
        {
            var pool = Enumerable.Range(0, run.PoolSize).ToList();

            var teams = RandomDraw.Teams(pool, run.Teams, new Random(run.Seed));

            teams.Should().HaveCount(run.Teams);
            teams.SelectMany(t => t).Order().Should().Equal(pool);
            (teams.Max(t => t.Count) - teams.Min(t => t.Count)).Should().BeLessThanOrEqualTo(1);
        });

    [Fact]
    public void Numbers_without_repeats_are_distinct_and_within_the_range() =>
        Gen.Select(Gen.Long[-1_000, 1_000], Gen.Int[0, 50], Gen.Int[1, 25], Gen.Int).Sample((min, span, count, seed) =>
        {
            var max = min + span;
            var wanted = Math.Min(count, span + 1);

            var drawn = RandomDraw.Numbers(min, max, wanted, allowRepeats: false, new Random(seed));

            drawn.Should().HaveCount(wanted).And.OnlyHaveUniqueItems().And.OnlyContain(n => n >= min && n <= max);
        });
}
