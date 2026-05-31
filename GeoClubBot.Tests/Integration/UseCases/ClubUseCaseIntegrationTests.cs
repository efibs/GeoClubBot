using Configuration;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.UseCases.Club;
using Xunit;
using DomainClub = Entities.Club;

namespace GeoClubBot.Tests.Integration.UseCases;

/// <summary>
/// Exercises the club use cases (level status broadcast, club lookup, today's XP) through the real
/// MediatR pipeline. GeoGuessr reads and the Discord status updater are substituted.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ClubUseCaseIntegrationTests(PostgresFixture fixture)
{
    /// <summary>Host whose GeoGuessr config has a single main club with the given id.</summary>
    private MediatorTestHost CreateHost(Guid mainClubId) =>
        new(fixture.ConnectionString, services =>
            services.AddSingleton(Options.Create(new GeoGuessrConfiguration
            {
                SyncSchedule = "0 0 0 * * ?",
                ActivityNcfaToken = "x",
                MissionsNcfaToken = "x",
                UserProfileNcfaToken = "x",
                Clubs =
                [
                    new GeoGuessrClubEntry { ClubId = mainClubId, NcfaToken = "x", IsMain = true },
                ],
            })));

    [Fact]
    public async Task SetClubLevelStatus_UpdatesTheDiscordStatus()
    {
        using var host = CreateHost(Guid.NewGuid());

        await host.SendAsync(new SetClubLevelStatusCommand(5));

        await host.Mock<IDiscordStatusUpdater>()
            .Received(1)
            .UpdateStatusAsync("Level 5 club!", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetClubByNameOrDefault_ReturnsTheMainClub_WhenNoNameGiven()
    {
        var mainClubId = Guid.NewGuid();
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(DomainClub.Create(mainClubId, $"club-{mainClubId:N}", 3));
            await seed.SaveChangesAsync();
        }

        using var host = CreateHost(mainClubId);
        var club = await host.SendAsync(new GetClubByNameOrDefaultQuery(null));

        club.Should().NotBeNull();
        club!.ClubId.Should().Be(mainClubId);
    }

    [Fact]
    public async Task GetClubByNameOrDefault_ReturnsTheNamedClub_WhenNameGiven()
    {
        var clubId = Guid.NewGuid();
        var name = $"named-{Guid.NewGuid():N}";
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(DomainClub.Create(clubId, name, 2));
            await seed.SaveChangesAsync();
        }

        using var host = CreateHost(Guid.NewGuid());
        var club = await host.SendAsync(new GetClubByNameOrDefaultQuery(name));

        club.Should().NotBeNull();
        club!.ClubId.Should().Be(clubId);
    }

    [Fact]
    public async Task GetClubByNameOrDefault_ReturnsNull_WhenMainClubIsNotTracked()
    {
        using var host = CreateHost(Guid.NewGuid());

        var club = await host.SendAsync(new GetClubByNameOrDefaultQuery(null));

        club.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetClubByNameOrDefault_FallsBackToMainClub_WhenNameIsBlank(string blankName)
    {
        // A blank/whitespace name must take the IsNullOrWhiteSpace → main-club branch, not be
        // treated as a real club name (which would look up "" / "   " and find nothing).
        var mainClubId = Guid.NewGuid();
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(DomainClub.Create(mainClubId, $"club-{mainClubId:N}", 3));
            await seed.SaveChangesAsync();
        }

        using var host = CreateHost(mainClubId);
        var club = await host.SendAsync(new GetClubByNameOrDefaultQuery(blankName));

        club.Should().NotBeNull();
        club!.ClubId.Should().Be(mainClubId);
    }

    [Fact]
    public async Task GetClubTodaysXp_ExcludesWeeklyMissions_WhenNotRequested()
    {
        var clubId = Guid.NewGuid();
        var name = $"xpclub-{Guid.NewGuid():N}";
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(DomainClub.Create(clubId, name, 1));
            await seed.SaveChangesAsync();
        }

        using var host = CreateHost(clubId);
        host.Mock<IGeoGuessrActivityReader>()
            .ReadTodaysActivitiesAsync(clubId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<ReadClubActivitiesItemDto>)
            [
                new ReadClubActivitiesItemDto { UserId = "u1", XpReward = 100, RecordedAt = DateTimeOffset.UtcNow },
                new ReadClubActivitiesItemDto { UserId = "u1", XpReward = 1000, RecordedAt = DateTimeOffset.UtcNow },
            ]);

        var excludingWeeklies = await host.SendAsync(new GetClubTodaysXpQuery(name, IncludeWeeklies: false));
        excludingWeeklies.Xp.Should().Be(100);
        excludingWeeklies.ClubName.Should().Be(name);

        var includingWeeklies = await host.SendAsync(new GetClubTodaysXpQuery(name, IncludeWeeklies: true));
        includingWeeklies.Xp.Should().Be(1100);
    }

    [Fact]
    public async Task GetClubTodaysXp_ReturnsNullResult_WhenClubIsUnknown()
    {
        using var host = CreateHost(Guid.NewGuid());

        var result = await host.SendAsync(new GetClubTodaysXpQuery($"missing-{Guid.NewGuid():N}", IncludeWeeklies: true));

        result.Xp.Should().BeNull();
        result.ClubName.Should().BeNull();
    }
}
