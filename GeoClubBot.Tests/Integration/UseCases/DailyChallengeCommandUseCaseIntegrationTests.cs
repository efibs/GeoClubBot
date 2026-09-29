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
using Xunit;

namespace GeoClubBot.Tests.Integration.UseCases;

/// <summary>
/// Exercises the daily-challenge posting use case end-to-end through the real MediatR pipeline.
/// The GeoGuessr API (challenge creation / highscores) and the Discord message + role access are
/// substituted; the persisted challenge links are asserted against Postgres.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class DailyChallengeCommandUseCaseIntegrationTests(PostgresFixture fixture)
{
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
                .SendMessageAsync(Arg.Is<string>(m => m!.Contains("Next challenges")), 5UL, Arg.Any<CancellationToken>());
        }
        finally
        {
            File.Delete(configFilePath);
        }
    }
}
