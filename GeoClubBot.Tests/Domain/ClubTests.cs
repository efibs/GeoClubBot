using Entities;
using FluentAssertions;
using Xunit;

namespace GeoClubBot.Tests.Domain;

public sealed class ClubTests
{
    [Fact]
    public void Create_SetsAllFields()
    {
        var id = Guid.NewGuid();
        var checkedAt = DateTimeOffset.UtcNow;

        var club = Club.Create(id, "My Club", level: 3, latestActivityCheckTime: checkedAt);

        club.ClubId.Should().Be(id);
        club.Name.Should().Be("My Club");
        club.Level.Should().Be(3);
        club.LatestActivityCheckTime.Should().Be(checkedAt);
    }

    [Fact]
    public void Create_DefaultsActivityCheckTimeToNull()
    {
        var club = Club.Create(Guid.NewGuid(), "Club", 1);

        club.LatestActivityCheckTime.Should().BeNull();
    }

    [Fact]
    public void UpdateLevel_ChangesLevel()
    {
        var club = Club.Create(Guid.NewGuid(), "Club", 1);

        club.UpdateLevel(5);

        club.Level.Should().Be(5);
    }

    [Fact]
    public void Rename_ChangesName()
    {
        var club = Club.Create(Guid.NewGuid(), "Old", 1);

        club.Rename("New");

        club.Name.Should().Be("New");
    }

    [Fact]
    public void RecordActivityCheck_StoresTimestamp()
    {
        var club = Club.Create(Guid.NewGuid(), "Club", 1);
        var now = DateTimeOffset.UtcNow;

        club.RecordActivityCheck(now);

        club.LatestActivityCheckTime.Should().Be(now);
    }

    [Fact]
    public void ToString_ReturnsName()
    {
        Club.Create(Guid.NewGuid(), "Club", 1).ToString().Should().Be("Club");
    }
}
