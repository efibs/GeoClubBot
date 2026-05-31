using CsCheck;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.PropertyBased;

/// <summary>
/// Property-based tests for the pure <see cref="TimeRange"/> algebra. Instead of a handful of
/// hand-picked examples, CsCheck throws thousands of randomised ranges at the invariants and
/// shrinks any failure to a minimal counterexample. All ranges are generated at UTC (offset
/// zero) so <c>.Ticks</c> comparisons line up with instant comparisons.
/// </summary>
public sealed class TimeRangePropertyTests
{
    // Leave generous head-room below DateTimeOffset.MaxValue so adding durations never overflows.
    private static readonly long MaxBaseTicks = DateTimeOffset.MaxValue.Ticks - TimeSpan.FromDays(800).Ticks;
    private static readonly long MaxDurationTicks = TimeSpan.FromDays(400).Ticks;

    private static DateTimeOffset Utc(long ticks) => new(ticks, TimeSpan.Zero);
    private static TimeRange Range(long from, long to) => new(Utc(from), Utc(to));

    private static readonly Gen<long> GenTick = Gen.Long[0L, MaxBaseTicks];

    private static readonly Gen<TimeRange> GenRange =
        Gen.Select(GenTick, Gen.Long[0L, MaxDurationTicks], (from, duration) => Range(from, from + duration));

    private static readonly Gen<(TimeRange, TimeRange)> GenRangePair =
        Gen.Select(GenRange, GenRange, (a, b) => (a, b));

    // Two ranges that are guaranteed to intersect: from four sorted ticks s0..s3, A = [s0, s2]
    // and B = [s1, s3] always share [s1, s2]. Required for the + and & operators, which throw
    // on non-intersecting inputs.
    private static readonly Gen<(TimeRange, TimeRange)> GenIntersectingPair =
        Gen.Select(GenTick, GenTick, GenTick, GenTick, (w, x, y, z) =>
        {
            var s = new[] { w, x, y, z };
            Array.Sort(s);
            return (Range(s[0], s[2]), Range(s[1], s[3]));
        });

    private static readonly Gen<List<TimeRange>> GenRangeList = GenRange.List[0, 8];

    [Fact]
    public void Intersects_is_symmetric() =>
        GenRangePair.Sample(pair => pair.Item1.Intersects(pair.Item2) == pair.Item2.Intersects(pair.Item1));

    [Fact]
    public void Contains_holds_at_both_endpoints_and_the_midpoint() =>
        GenRange.Sample(r =>
        {
            var mid = Utc(r.From.Ticks + (r.To.Ticks - r.From.Ticks) / 2);
            return r.Contains(r.From) && r.Contains(r.To) && r.Contains(mid);
        });

    [Fact]
    public void Union_is_the_bounding_range_of_both_operands() =>
        GenIntersectingPair.Sample(pair =>
        {
            var (a, b) = pair;
            var union = a + b;
            return union.From == (a.From < b.From ? a.From : b.From)
                   && union.To == (a.To > b.To ? a.To : b.To);
        });

    [Fact]
    public void Union_contains_every_point_of_both_operands() =>
        GenIntersectingPair.Sample(pair =>
        {
            var (a, b) = pair;
            var union = a + b;
            return union.From <= a.From && union.To >= a.To
                   && union.From <= b.From && union.To >= b.To;
        });

    [Fact]
    public void Union_is_commutative() =>
        GenIntersectingPair.Sample(pair => (pair.Item1 + pair.Item2) == (pair.Item2 + pair.Item1));

    [Fact]
    public void Intersection_lies_within_both_operands() =>
        GenIntersectingPair.Sample(pair =>
        {
            var (a, b) = pair;
            var inter = a & b;
            return inter.From == (a.From > b.From ? a.From : b.From)
                   && inter.To == (a.To < b.To ? a.To : b.To)
                   && a.From <= inter.From && inter.To <= a.To
                   && b.From <= inter.From && inter.To <= b.To;
        });

    [Fact]
    public void Intersection_is_commutative() =>
        GenIntersectingPair.Sample(pair => (pair.Item1 & pair.Item2) == (pair.Item2 & pair.Item1));

    [Fact]
    public void Intersection_is_contained_in_the_union() =>
        GenIntersectingPair.Sample(pair =>
        {
            var (a, b) = pair;
            var inter = a & b;
            var union = a + b;
            return union.From <= inter.From && inter.To <= union.To;
        });

    [Fact]
    public void Squash_produces_pairwise_disjoint_ranges() =>
        GenRangeList.Sample(ranges =>
        {
            var squashed = TimeRange.Squash(ranges);
            for (var i = 0; i < squashed.Count; i++)
            {
                for (var j = i + 1; j < squashed.Count; j++)
                {
                    if (squashed[i].Intersects(squashed[j]))
                    {
                        return false;
                    }
                }
            }

            return true;
        });

    [Fact]
    public void Squash_covers_every_input_range() =>
        GenRangeList.Sample(ranges =>
        {
            var squashed = TimeRange.Squash(ranges);
            return ranges.All(input =>
                squashed.Any(s => s.From <= input.From && s.To >= input.To));
        });

    [Fact]
    public void Squash_is_idempotent() =>
        GenRangeList.Sample(ranges =>
        {
            var once = TimeRange.Squash(ranges);
            var twice = TimeRange.Squash(once);
            return once.SequenceEqual(twice);
        });

    [Fact]
    public void Squash_never_increases_the_total_covered_span() =>
        GenRangeList.Sample(ranges =>
        {
            var inputTotal = ranges.Aggregate(TimeSpan.Zero, (acc, r) => acc + r.ToTimeSpan());
            var squashedTotal = TimeRange.Squash(ranges).Aggregate(TimeSpan.Zero, (acc, r) => acc + r.ToTimeSpan());
            return squashedTotal <= inputTotal && TimeRange.Squash(ranges).Count <= ranges.Count;
        });
}
