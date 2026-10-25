using FluentAssertions;
using GeoClubBot.MockGeoGuessr.Client;
using GeoClubBot.MockGeoGuessr.DataStore;
using UseCases.OutputPorts.GeoGuessr;
using Xunit;

namespace GeoClubBot.Tests.Tools;

public sealed class MockGeoGuessrApplicationsTests
{
    private static readonly Guid ClubId = Guid.NewGuid();
    private readonly MockGeoGuessrDataStore _store = new();

    [Fact]
    public async Task Applications_AreReturnedPerClub_UntilRemoved()
    {
        var applications = _store.ClubApplications.GetOrAdd(ClubId, _ => new());
        applications["u1"] = new ClubMemberUserDto
        {
            UserId = "u1",
            Nick = "Alice",
            Avatar = "",
            FullBodyAvatar = "",
            BorderUrl = "",
            IsVerified = false,
            Flair = 0,
            CountryCode = "de",
            TierId = 0,
            ClubUserType = 3
        };
        var client = new MockGeoGuessrClient(_store, ClubId);

        (await client.ReadClubApplicationsAsync(ClubId)).Items.Should().ContainSingle().Which.Nick.Should().Be("Alice");
        (await client.ReadClubApplicationsAsync(Guid.NewGuid())).Items.Should().BeEmpty();

        applications.TryRemove("u1", out _);
        (await client.ReadClubApplicationsAsync(ClubId)).Items.Should().BeEmpty();
    }
}
