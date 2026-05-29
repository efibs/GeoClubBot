using Entities;
using FluentAssertions;
using Xunit;

namespace GeoClubBot.Tests.Domain;

public sealed class ClubMemberExcuseTests
{
    private static readonly DateTimeOffset From = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2025, 1, 8, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_SetsFields_WithGeneratedId()
    {
        var excuse = ClubMemberExcuse.Create("user-1", From, To);

        excuse.UserId.Should().Be("user-1");
        excuse.From.Should().Be(From);
        excuse.To.Should().Be(To);
        excuse.ExcuseId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Create_WhenFromEqualsTo_Throws()
    {
        var act = () => ClubMemberExcuse.Create("u", From, From);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WhenFromAfterTo_Throws()
    {
        var act = () => ClubMemberExcuse.Create("u", To, From);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UpdateTimeRange_ChangesBounds()
    {
        var excuse = ClubMemberExcuse.Create("u", From, To);
        var newTo = To.AddDays(7);

        excuse.UpdateTimeRange(From, newTo);

        excuse.To.Should().Be(newTo);
    }

    [Fact]
    public void UpdateTimeRange_WhenInvalid_Throws()
    {
        var excuse = ClubMemberExcuse.Create("u", From, To);

        var act = () => excuse.UpdateTimeRange(To, From);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0, true)]   // exactly From
    [InlineData(3, true)]   // inside
    [InlineData(7, true)]   // exactly To
    [InlineData(-1, false)] // before
    [InlineData(8, false)]  // after
    public void Covers_RespectsInclusiveBounds(int dayOffset, bool expected)
    {
        var excuse = ClubMemberExcuse.Create("u", From, To);

        excuse.Covers(From.AddDays(dayOffset)).Should().Be(expected);
    }
}
