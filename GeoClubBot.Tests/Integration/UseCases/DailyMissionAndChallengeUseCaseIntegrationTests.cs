using System.Text.Json;
using Configuration;
using Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.UseCases.DailyChallenge;
using UseCases.UseCases.DailyMissionLogging;
using Xunit;

namespace GeoClubBot.Tests.Integration.UseCases;

/// <summary>
/// Exercises the daily-mission-logging and daily-challenge use cases end-to-end through the real
/// MediatR pipeline. The GeoGuessr API (missions feed / challenge creation / highscores) and the
/// Discord message + role access are substituted; the persisted missions / challenge links are
/// asserted against Postgres.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class DailyMissionAndChallengeUseCaseIntegrationTests(PostgresFixture fixture)
{
    private static DailyMissionDto BuildMission(string type, string gameMode, int target, string? mapSlug = null, string? mapName = null) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        GameMode = gameMode,
        CurrentProgress = 0,
        TargetProgress = target,
        Completed = false,
        EndDate = DateTimeOffset.UtcNow.AddDays(1),
        RewardAmount = 100,
        RewardType = "Xp",
        MapSlug = mapSlug,
        MapName = mapName,
    };

    private MediatorTestHost CreateMissionHost() =>
        new(fixture.ConnectionString, services =>
            services.AddSingleton(Options.Create(new DailyMissionLoggingConfiguration
            {
                Schedule = "0 0 0 * * ?",
                ReadableChannelId = 10,
                ReadableFormat = "# Daily missions\n{{missionText}}",
                LookupChannelId = 20,
                LookupFormat = "{{missionText}}",
            })));

    [Fact]
    public async Task LogDailyMissions_PersistsTheMissions_AndPostsToBothChannels()
    {
        var missions = new List<DailyMissionDto>
        {
            BuildMission("PlayGames", "Duels", 3),
            BuildMission("Score", "Classic", 15000),
        };
        var missionIds = missions.Select(m => m.Id).ToList();

        using var host = CreateMissionHost();
        var missionsClient = Substitute.For<IGeoGuessrClient>();
        missionsClient.ReadDailyMissionsAsync(Arg.Any<CancellationToken>())
            .Returns(new DailyMissionsResponseDto { Missions = missions, NextMissionDate = DateTimeOffset.UtcNow.AddDays(1) });
        host.Mock<IGeoGuessrClientFactory>().CreateMissionsClient().Returns(missionsClient);

        await host.SendAsync(new LogDailyMissionsCommand());

        await using var read = fixture.CreateDbContext();
        var persisted = await read.DailyMissions.AsNoTracking()
            .Where(m => missionIds.Contains(m.MissionId))
            .ToListAsync();
        persisted.Should().HaveCount(2);

        await host.Mock<IDiscordMessageAccess>()
            .Received()
            .SendMessageAsync(Arg.Any<string>(), 10UL, Arg.Any<CancellationToken>());
        await host.Mock<IDiscordMessageAccess>()
            .Received()
            .SendMessageAsync(Arg.Any<string>(), 20UL, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LogDailyMissions_PersistsMapProperties_WhenProvided()
    {
        var mission = BuildMission("PlayGames", "Duels", 3, mapSlug: "a-custom-map", mapName: "A Custom Map");

        using var host = CreateMissionHost();
        var missionsClient = Substitute.For<IGeoGuessrClient>();
        missionsClient.ReadDailyMissionsAsync(Arg.Any<CancellationToken>())
            .Returns(new DailyMissionsResponseDto { Missions = [mission], NextMissionDate = DateTimeOffset.UtcNow.AddDays(1) });
        host.Mock<IGeoGuessrClientFactory>().CreateMissionsClient().Returns(missionsClient);

        await host.SendAsync(new LogDailyMissionsCommand());

        await using var read = fixture.CreateDbContext();
        var persisted = await read.DailyMissions.AsNoTracking()
            .SingleAsync(m => m.MissionId == mission.Id);
        persisted.MapSlug.Should().Be("a-custom-map");
        persisted.MapName.Should().Be("A Custom Map");
    }

    [Fact]
    public async Task LogDailyMissions_PersistsNullMapProperties_WhenNotProvided()
    {
        var mission = BuildMission("Score", "Classic", 15000);

        using var host = CreateMissionHost();
        var missionsClient = Substitute.For<IGeoGuessrClient>();
        missionsClient.ReadDailyMissionsAsync(Arg.Any<CancellationToken>())
            .Returns(new DailyMissionsResponseDto { Missions = [mission], NextMissionDate = DateTimeOffset.UtcNow.AddDays(1) });
        host.Mock<IGeoGuessrClientFactory>().CreateMissionsClient().Returns(missionsClient);

        await host.SendAsync(new LogDailyMissionsCommand());

        await using var read = fixture.CreateDbContext();
        var persisted = await read.DailyMissions.AsNoTracking()
            .SingleAsync(m => m.MissionId == mission.Id);
        persisted.MapSlug.Should().BeNull();
        persisted.MapName.Should().BeNull();
    }

    [Fact]
    public async Task LogDailyMissions_IsANoOp_WhenTheMissionsFeedFails()
    {
        using var host = CreateMissionHost();
        var missionsClient = Substitute.For<IGeoGuessrClient>();
        missionsClient.ReadDailyMissionsAsync(Arg.Any<CancellationToken>())
            .Returns<DailyMissionsResponseDto>(_ => throw new InvalidOperationException("boom"));
        host.Mock<IGeoGuessrClientFactory>().CreateMissionsClient().Returns(missionsClient);

        await host.SendAsync(new LogDailyMissionsCommand());

        await host.Mock<IDiscordMessageAccess>()
            .DidNotReceive()
            .SendMessageAsync(Arg.Any<string>(), Arg.Any<ulong>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DailyChallenge_CreatesChallenges_PersistsLinks_AndPostsTheNextChallenges()
    {
        var difficulty = $"Diff-{Guid.NewGuid():N}"[..16];
        var challengeConfig = new List<ClubChallengeConfigurationDifficulty>
        {
            new(difficulty)
            {
                RolePriority = 1,
                Entries =
                [
                    new ClubChallengeConfigurationDifficultyEntry(
                        Description: "A world map", MapId: "world", ForbidMoving: true,
                        ForbidRotating: false, ForbidZooming: false, TimeLimit: 60),
                ],
            },
        };

        var configFilePath = Path.Combine(Path.GetTempPath(), $"challenges-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(configFilePath, JsonSerializer.Serialize(challengeConfig));

        try
        {
            var mainClubId = Guid.NewGuid();
            using var host = new MediatorTestHost(
                fixture.ConnectionString,
                services =>
                {
                    services.AddSingleton(Options.Create(new GeoGuessrConfiguration
                    {
                        SyncSchedule = "0 0 0 * * ?",
                        ActivityNcfaToken = "x",
                        MissionsNcfaToken = "x",
                        UserProfileNcfaToken = "x",
                        Clubs = [new GeoGuessrClubEntry { ClubId = mainClubId, NcfaToken = "x", IsMain = true }],
                    }));
                    services.AddSingleton(Options.Create(new DailyChallengesConfiguration
                    {
                        Schedule = "0 0 0 * * ?",
                        TextChannelId = 5,
                        ConfigurationFilePath = configFilePath,
                        FirstRoleId = 100,
                        SecondRoleId = 200,
                        ThirdRoleId = 300,
                    }));
                });

            var client = Substitute.For<IGeoGuessrClient>();
            client.CreateChallengeAsync(Arg.Any<PostChallengeRequestDto>(), Arg.Any<CancellationToken>())
                .Returns(new PostChallengeResponseDto { Token = "challenge-token" });
            // Defensive: any pre-existing links would trigger a highscore read.
            client.ReadHighscoresAsync(Arg.Any<string>(), Arg.Any<ReadHighscoresQueryParams>(), Arg.Any<CancellationToken>())
                .Returns(new ChallengeResultHighscoresDto { Items = [] });
            host.Mock<IGeoGuessrClientFactory>().CreateClient(mainClubId).Returns(client);

            await host.SendAsync(new DailyChallengeCommand());

            await using var read = fixture.CreateDbContext();
            var links = await read.LatestClubChallengeLinks.AsNoTracking()
                .Where(l => l.Difficulty == difficulty)
                .ToListAsync();
            links.Should().ContainSingle()
                .Which.ChallengeId.Should().Be("challenge-token");

            await host.Mock<IDiscordMessageAccess>()
                .Received()
                .SendMessageAsync(Arg.Is<string>(m => m.Contains("Next challenges")), 5UL, Arg.Any<CancellationToken>());
        }
        finally
        {
            File.Delete(configFilePath);
        }
    }
}
