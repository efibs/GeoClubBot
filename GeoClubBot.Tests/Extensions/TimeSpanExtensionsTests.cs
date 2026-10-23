using Extensions;
using FluentAssertions;
using Xunit;

namespace GeoClubBot.Tests.ExtensionMethods;

public sealed class TimeSpanExtensionsTests
{
    [Theory]
    [InlineData(0, "<1m")]
    [InlineData(59, "<1m")]
    [InlineData(35 * 60, "35m")]
    [InlineData(4 * 3600 + 12 * 60, "4h 12m")]
    [InlineData(5 * 3600, "5h")]
    [InlineData(51 * 3600, "2d 3h")]
    [InlineData(48 * 3600 + 54 * 60, "2d")]
    [InlineData(-3600, "<1m")]
    public void ToShortDuration_ShowsTheTwoLargestUnits(int seconds, string expected) =>
        TimeSpan.FromSeconds(seconds).ToShortDuration().Should().Be(expected);

    [Theory]
    [InlineData(14 * 24, "14 days")]
    [InlineData(24, "day")]
    [InlineData(36, "1d 12h")]
    public void ToReadableSpan_SpellsOutWholeDays(int hours, string expected) =>
        TimeSpan.FromHours(hours).ToReadableSpan().Should().Be(expected);
}
