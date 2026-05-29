using Extensions;
using FluentAssertions;
using Xunit;

namespace GeoClubBot.Tests.ExtensionMethods;

public sealed class DateTimeOffsetExtensionsTests
{
    [Fact]
    public void Truncate_RoundsDownToInterval()
    {
        var input = new DateTimeOffset(2025, 1, 1, 13, 47, 32, TimeSpan.Zero);

        var truncated = input.Truncate(TimeSpan.FromHours(1));

        truncated.Should().Be(new DateTimeOffset(2025, 1, 1, 13, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Truncate_OnExactBoundary_ReturnsSameInstant()
    {
        var input = new DateTimeOffset(2025, 1, 1, 13, 0, 0, TimeSpan.Zero);

        input.Truncate(TimeSpan.FromHours(1)).Should().Be(input);
    }

    [Fact]
    public void Truncate_NormalizesToUtcOffset()
    {
        // Truncate builds the result from raw ticks with a zero offset.
        var input = new DateTimeOffset(2025, 1, 1, 13, 47, 0, TimeSpan.FromHours(2));

        var truncated = input.Truncate(TimeSpan.FromMinutes(30));

        truncated.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Truncate_ZeroInterval_Throws()
    {
        var input = DateTimeOffset.UtcNow;

        var act = () => input.Truncate(TimeSpan.Zero);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("interval");
    }

    [Fact]
    public void RoundUp_AdvancesToNextInterval()
    {
        var input = new DateTimeOffset(2025, 1, 1, 13, 47, 0, TimeSpan.Zero);

        var rounded = input.RoundUp(TimeSpan.FromHours(1));

        rounded.Should().Be(new DateTimeOffset(2025, 1, 1, 14, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void RoundUp_OnExactBoundary_AdvancesAFullInterval()
    {
        // RoundUp truncates then always adds one interval.
        var input = new DateTimeOffset(2025, 1, 1, 13, 0, 0, TimeSpan.Zero);

        var rounded = input.RoundUp(TimeSpan.FromHours(1));

        rounded.Should().Be(new DateTimeOffset(2025, 1, 1, 14, 0, 0, TimeSpan.Zero));
    }
}
