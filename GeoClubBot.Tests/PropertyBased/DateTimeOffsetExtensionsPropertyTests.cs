using CsCheck;
using Extensions;
using FluentAssertions;
using Xunit;

namespace GeoClubBot.Tests.PropertyBased;

/// <summary>
/// Property-based tests for the date-window helpers used to align activity-check intervals.
/// <see cref="DateTimeOffsetExtensions.Truncate"/> floors a timestamp to an interval boundary
/// and <see cref="DateTimeOffsetExtensions.RoundUp"/> ceilings it; both should be exactly
/// aligned and within one interval of the input. Inputs are UTC so <c>.Ticks</c> is the instant.
/// </summary>
public sealed class DateTimeOffsetExtensionsPropertyTests
{
    // Head-room so RoundUp (truncate + interval) never overflows DateTimeOffset.MaxValue.
    private static readonly long MaxBaseTicks = DateTimeOffset.MaxValue.Ticks - TimeSpan.FromDays(800).Ticks;
    private static readonly long MaxIntervalTicks = TimeSpan.FromDays(366).Ticks;

    private static readonly Gen<(DateTimeOffset, TimeSpan)> GenTimeAndInterval =
        Gen.Select(
            Gen.Long[0L, MaxBaseTicks].Select(ticks => new DateTimeOffset(ticks, TimeSpan.Zero)),
            Gen.Long[1L, MaxIntervalTicks].Select(TimeSpan.FromTicks),
            (time, interval) => (time, interval));

    [Fact]
    public void Truncate_is_aligned_and_at_or_before_the_input_within_one_interval() =>
        GenTimeAndInterval.Sample(input =>
        {
            var (time, interval) = input;
            var truncated = time.Truncate(interval);

            return truncated.Ticks % interval.Ticks == 0          // lands on a boundary
                   && truncated.Ticks <= time.Ticks               // never moves forward
                   && time.Ticks - truncated.Ticks < interval.Ticks; // by less than one interval
        });

    [Fact]
    public void Truncate_is_idempotent() =>
        GenTimeAndInterval.Sample(input =>
        {
            var (time, interval) = input;
            var once = time.Truncate(interval);
            return once.Truncate(interval) == once;
        });

    [Fact]
    public void RoundUp_equals_truncate_plus_one_interval_and_is_aligned() =>
        GenTimeAndInterval.Sample(input =>
        {
            var (time, interval) = input;
            var roundedUp = time.RoundUp(interval);

            return roundedUp == time.Truncate(interval).Add(interval)
                   && roundedUp.Ticks % interval.Ticks == 0;
        });

    [Fact]
    public void RoundUp_is_after_the_input_by_at_most_one_interval() =>
        GenTimeAndInterval.Sample(input =>
        {
            var (time, interval) = input;
            var delta = time.RoundUp(interval).Ticks - time.Ticks;
            return delta > 0 && delta <= interval.Ticks;
        });

    [Fact]
    public void Truncate_throws_on_a_zero_interval()
    {
        var act = () => DateTimeOffset.UnixEpoch.Truncate(TimeSpan.Zero);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
