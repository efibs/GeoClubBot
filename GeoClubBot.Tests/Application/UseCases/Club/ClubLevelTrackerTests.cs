using Entities;
using FluentAssertions;
using NSubstitute;
using UseCases.OutputPorts;
using UseCases.UseCases.Club;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.Club;

public sealed class ClubLevelTrackerTests
{
    private readonly IClubRepository _clubs = Substitute.For<IClubRepository>();

    private static readonly Guid ClubA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClubB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void TryGet_ReturnsNull_ForUnknownClub()
    {
        new ClubLevelTracker().TryGet(ClubA).Should().BeNull();
    }

    [Fact]
    public void Set_StoresLevel_RetrievableByTryGet()
    {
        var tracker = new ClubLevelTracker();

        tracker.Set(ClubA, 7);

        tracker.TryGet(ClubA).Should().Be(7);
    }

    [Fact]
    public void Set_OverwritesPreviousLevel()
    {
        var tracker = new ClubLevelTracker();
        tracker.Set(ClubA, 1);

        tracker.Set(ClubA, 9);

        tracker.TryGet(ClubA).Should().Be(9);
    }

    [Fact]
    public async Task EnsureInitialized_LoadsLevelsFromRepository()
    {
        _clubs.ReadClubByIdAsync(ClubA, Arg.Any<CancellationToken>())
            .Returns(global::Entities.Club.Create(ClubA, "A", 4));
        _clubs.ReadClubByIdAsync(ClubB, Arg.Any<CancellationToken>())
            .Returns(global::Entities.Club.Create(ClubB, "B", 8));
        var tracker = new ClubLevelTracker();

        await tracker.EnsureInitializedAsync(_clubs, [ClubA, ClubB]);

        tracker.TryGet(ClubA).Should().Be(4);
        tracker.TryGet(ClubB).Should().Be(8);
    }

    [Fact]
    public async Task EnsureInitialized_SkipsClubsNotFound()
    {
        _clubs.ReadClubByIdAsync(ClubA, Arg.Any<CancellationToken>()).Returns((global::Entities.Club?)null);
        var tracker = new ClubLevelTracker();

        await tracker.EnsureInitializedAsync(_clubs, [ClubA]);

        tracker.TryGet(ClubA).Should().BeNull();
    }

    [Fact]
    public async Task EnsureInitialized_OnlyRunsOnce()
    {
        _clubs.ReadClubByIdAsync(ClubA, Arg.Any<CancellationToken>())
            .Returns(global::Entities.Club.Create(ClubA, "A", 4));
        var tracker = new ClubLevelTracker();

        await tracker.EnsureInitializedAsync(_clubs, [ClubA]);
        await tracker.EnsureInitializedAsync(_clubs, [ClubA]);

        await _clubs.Received(1).ReadClubByIdAsync(ClubA, Arg.Any<CancellationToken>());
    }
}
