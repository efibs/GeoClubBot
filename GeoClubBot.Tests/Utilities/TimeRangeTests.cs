using FluentAssertions;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Utilities;

public sealed class TimeRangeTests
{
    private static readonly DateTimeOffset T0 = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static TimeRange Range(int fromHours, int toHours) =>
        new(T0.AddHours(fromHours), T0.AddHours(toHours));

    [Fact]
    public void DefaultConstructor_SpansEntireRange()
    {
        var range = new TimeRange();

        range.From.Should().Be(DateTimeOffset.MinValue);
        range.To.Should().Be(DateTimeOffset.MaxValue);
    }

    [Fact]
    public void Intersects_ReturnsTrue_WhenOverlapping()
    {
        Range(0, 4).Intersects(Range(2, 6)).Should().BeTrue();
    }

    [Fact]
    public void Intersects_ReturnsTrue_WhenTouchingAtBoundary()
    {
        // From <= other.To && To >= other.From — touching endpoints count as intersecting.
        Range(0, 2).Intersects(Range(2, 4)).Should().BeTrue();
    }

    [Fact]
    public void Intersects_ReturnsFalse_WhenDisjoint()
    {
        Range(0, 2).Intersects(Range(3, 5)).Should().BeFalse();
    }

    [Fact]
    public void Intersects_IsSymmetric()
    {
        var a = Range(0, 4);
        var b = Range(2, 6);

        a.Intersects(b).Should().Be(b.Intersects(a));
    }

    [Theory]
    [InlineData(0, true)]   // on From boundary
    [InlineData(2, true)]   // inside
    [InlineData(4, true)]   // on To boundary
    [InlineData(-1, false)] // before
    [InlineData(5, false)]  // after
    public void Contains_RespectsInclusiveBoundaries(int hour, bool expected)
    {
        Range(0, 4).Contains(T0.AddHours(hour)).Should().Be(expected);
    }

    [Fact]
    public void ToTimeSpan_ReturnsDuration()
    {
        Range(1, 4).ToTimeSpan().Should().Be(TimeSpan.FromHours(3));
    }

    [Fact]
    public void UnionOperator_ReturnsOuterBounds_WhenIntersecting()
    {
        var union = Range(0, 4) + Range(2, 6);

        union.From.Should().Be(T0);
        union.To.Should().Be(T0.AddHours(6));
    }

    [Fact]
    public void UnionOperator_Throws_WhenDisjoint()
    {
        var act = () => _ = Range(0, 2) + Range(5, 7);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void IntersectionOperator_ReturnsInnerBounds_WhenIntersecting()
    {
        var intersection = Range(0, 4) & Range(2, 6);

        intersection.From.Should().Be(T0.AddHours(2));
        intersection.To.Should().Be(T0.AddHours(4));
    }

    [Fact]
    public void IntersectionOperator_Throws_WhenDisjoint()
    {
        var act = () => _ = Range(0, 2) & Range(5, 7);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Squash_EmptyList_ReturnsEmpty()
    {
        TimeRange.Squash([]).Should().BeEmpty();
    }

    [Fact]
    public void Squash_MergesOverlappingRanges()
    {
        var squashed = TimeRange.Squash([Range(0, 3), Range(2, 5)]);

        squashed.Should().ContainSingle();
        squashed[0].From.Should().Be(T0);
        squashed[0].To.Should().Be(T0.AddHours(5));
    }

    [Fact]
    public void Squash_MergesTouchingRanges()
    {
        // Touching ranges intersect, so they are merged.
        var squashed = TimeRange.Squash([Range(0, 2), Range(2, 4)]);

        squashed.Should().ContainSingle();
        squashed[0].To.Should().Be(T0.AddHours(4));
    }

    [Fact]
    public void Squash_KeepsDisjointRangesSeparate()
    {
        var squashed = TimeRange.Squash([Range(0, 1), Range(3, 4)]);

        squashed.Should().HaveCount(2);
    }

    [Fact]
    public void Squash_HandlesUnorderedInput()
    {
        var squashed = TimeRange.Squash([Range(4, 6), Range(0, 2), Range(1, 5)]);

        squashed.Should().ContainSingle();
        squashed[0].From.Should().Be(T0);
        squashed[0].To.Should().Be(T0.AddHours(6));
    }

    [Fact]
    public void CalculateFreePercent_NoBlockingRanges_IsFullyFree()
    {
        Range(0, 10).CalculateFreePercent([]).Should().Be(1.0);
    }

    [Fact]
    public void CalculateFreePercent_FullyBlocked_IsZero()
    {
        Range(0, 10).CalculateFreePercent([Range(0, 10)]).Should().Be(0.0);
    }

    [Fact]
    public void CalculateFreePercent_HalfBlocked_IsHalf()
    {
        Range(0, 10).CalculateFreePercent([Range(0, 5)]).Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void CalculateFreePercent_SquashesOverlappingBlocksBeforeSumming()
    {
        // Two overlapping 0-5 and 3-6 blocks cover 0-6 (6h), not 8h.
        Range(0, 10).CalculateFreePercent([Range(0, 5), Range(3, 6)])
            .Should().BeApproximately(0.4, 1e-9);
    }
}
