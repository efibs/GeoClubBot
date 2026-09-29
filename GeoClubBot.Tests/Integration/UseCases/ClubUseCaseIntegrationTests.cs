using Configuration;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
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
    public async Task GetClubTodaysXp_SumsEveryKindOfXp()
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
                ClubActivities.Untyped("u1", xpReward: 100),
                ClubActivities.BoardBonus("u1"),
            ]);

        var result = await host.SendAsync(new GetClubTodaysXpQuery(name));

        result.Xp.Should().Be(200);
        result.ClubName.Should().Be(name);
    }

    [Fact]
    public async Task GetClubTodaysXp_CountsStreakMissionsAndClaimsSeparately_AndClubSize()
    {
        var clubId = Guid.NewGuid();
        var name = $"xpclub-{Guid.NewGuid():N}";

        // Three members in the club.
        var userIds = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid().ToString("N")[..24]).ToArray();
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(DomainClub.Create(clubId, name, 1));

            foreach (var userId in userIds)
            {
                var user = Entities.GeoGuessrUser.Create(userId, $"nick-{Guid.NewGuid():N}"[..30]);
                seed.Add(Entities.ClubMember.Create(user, clubId, xp: 0, joinedAt: DateTimeOffset.UtcNow.AddMonths(-1)));
            }

            await seed.SaveChangesAsync();
        }

        using var host = CreateHost(clubId);
        host.Mock<IGeoGuessrActivityReader>()
            .ReadTodaysActivitiesAsync(clubId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<ReadClubActivitiesItemDto>)
            [
                // The first member kept the streak and finished two missions.
                ClubActivities.BoardMission(userIds[0]),
                ClubActivities.BoardMission(userIds[0]),
                ClubActivities.Challenge(userIds[0]),
                ClubActivities.BoardMission(userIds[1]),
                // Zero-XP club challenge: neither the streak nor a mission.
                ClubActivities.ClubChallenge(userIds[2]),
            ]);
        host.Mock<IClubMissionBoardReader>()
            .ReadCurrentAsync(clubId, Arg.Any<CancellationToken>())
            .Returns(MissionBoards.Week(
                MissionBoards.Board(1,
                    MissionBoards.Tile(index: 0, claimedBy: userIds[0], claimedAt: DateTimeOffset.UtcNow.AddMinutes(-5)),
                    MissionBoards.Tile(index: 1, claimedBy: userIds[1], claimedAt: DateTimeOffset.UtcNow.AddDays(-2), completed: true)),
                nextClaimResetAt: DateTimeOffset.UtcNow.AddHours(1)));

        var result = await host.SendAsync(new GetClubTodaysXpQuery(name));

        result.BoardMissionCount.Should().Be(3);
        result.ChallengeMemberCount.Should().Be(1);
        result.ClaimMemberCount.Should().Be(1, "only the first member claimed in the current claim cycle");
        result.TotalMemberCount.Should().Be(3);
    }

    [Fact]
    public async Task GetClubTodaysXp_ReturnsNullResult_WhenClubIsUnknown()
    {
        using var host = CreateHost(Guid.NewGuid());

        var result = await host.SendAsync(new GetClubTodaysXpQuery($"missing-{Guid.NewGuid():N}"));

        result.Xp.Should().BeNull();
        result.ClubName.Should().BeNull();
        result.BoardMissionCount.Should().BeNull();
        result.ClaimMemberCount.Should().BeNull();
        result.ChallengeMemberCount.Should().BeNull();
        result.TotalMemberCount.Should().BeNull();
    }
}
