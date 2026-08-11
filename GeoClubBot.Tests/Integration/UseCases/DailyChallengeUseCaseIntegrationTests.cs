using System.Text.Json;
using Configuration;
using Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.UseCases.DailyChallenge;
using Xunit;

namespace GeoClubBot.Tests.Integration.UseCases;

/// <summary>
/// Exercises the daily challenge through the real MediatR pipeline and Postgres, covering the two
/// things only a real unit of work can show: players the bot has never seen before are synced into
/// <see cref="GeoGuessrUser"/> while the roles are handed out (and the same player placing in
/// several challenges must not be inserted twice), and which challenge links survive a run in which
/// GeoGuessr refused to create some — or all — of the next challenges.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class DailyChallengeUseCaseIntegrationTests(PostgresFixture fixture)
{
    private const ulong FirstRoleId = 100;
    private const ulong SecondRoleId = 200;
    private const ulong ThirdRoleId = 300;
    private const ulong TextChannelId = 5;

    private static string NewUserId() => Guid.NewGuid().ToString("N")[..24];
    private static ulong NewDiscordId() => (ulong)Random.Shared.NextInt64(1_000_000_000_000_000L, long.MaxValue);

    /// <summary>
    /// Regression test for the daily "Unexpected entry.EntityState: Detached" crash: an unknown
    /// player placing in two challenges was synced twice into the same DbContext. The second insert
    /// hit an identity conflict that left a detached zombie entry behind, which then blew up the
    /// unit of work commit for the whole daily challenge.
    /// </summary>
    [Fact]
    public async Task DistributeRoles_SyncsAnUnknownPlayerOnce_WhenTheyPlacedInSeveralChallenges()
    {
        var newcomerId = NewUserId();
        var regularId = NewUserId();

        using var host = CreateHost();
        ArrangeUserProfiles(host);

        var results = new List<ClubChallengeResult>
        {
            new("Hard", 2, [Player(newcomerId)]),
            new("Easy", 1, [Player(regularId), Player(newcomerId)]),
        };

        await host.SendAsync(new DistributeDailyChallengeRolesCommand(results));

        await using var read = fixture.CreateDbContext();
        var persisted = await read.GeoGuessrUsers.AsNoTracking()
            .Where(u => u.UserId == newcomerId || u.UserId == regularId)
            .Select(u => u.UserId)
            .ToListAsync();

        persisted.Should().BeEquivalentTo([newcomerId, regularId],
            "both unknown players are synced exactly once, even though the newcomer placed twice");
    }

    /// <summary>
    /// The same scenario driven from the top: the whole daily challenge (create challenges, publish
    /// the results, hand out the roles) has to commit even when the previous challenge was played by
    /// someone the bot has never seen — that is the production failure this regressed on.
    /// </summary>
    [Fact]
    public async Task DailyChallenge_CommitsTheNewLinks_WhenAnUnknownPlayerPlacedInSeveralChallenges()
    {
        var newcomerId = NewUserId();
        var regularId = NewUserId();
        var hard = $"Hard-{Guid.NewGuid():N}"[..16];
        var easy = $"Easy-{Guid.NewGuid():N}"[..16];
        var hardToken = $"hard-{Guid.NewGuid():N}"[..16];
        var easyToken = $"easy-{Guid.NewGuid():N}"[..16];

        await SeedChallengeLinksAsync((hard, 2, hardToken), (easy, 1, easyToken));

        var configFilePath = await WriteChallengeConfigAsync((hard, 2), (easy, 1));
        try
        {
            var (host, client) = CreateChallengeHost(configFilePath);
            using var _ = host;

            // The newcomer won yesterday's hard challenge and came second in the easy one, so they
            // are resolved twice while the podium roles are handed out.
            client.ReadHighscoresAsync(hardToken, Arg.Any<ReadHighscoresQueryParams>(), Arg.Any<CancellationToken>())
                .Returns(Highscores(newcomerId));
            client.ReadHighscoresAsync(easyToken, Arg.Any<ReadHighscoresQueryParams>(), Arg.Any<CancellationToken>())
                .Returns(Highscores(regularId, newcomerId));

            await host.SendAsync(new DailyChallengeCommand());

            await using var read = fixture.CreateDbContext();
            var links = await read.LatestClubChallengeLinks.AsNoTracking()
                .Where(l => l.Difficulty == hard || l.Difficulty == easy)
                .ToListAsync();
            links.Should().HaveCount(2, "yesterday's links are replaced by today's")
                .And.OnlyContain(l => l.ChallengeId == "new-token");

            var syncedPlayers = await read.GeoGuessrUsers.AsNoTracking()
                .Where(u => u.UserId == newcomerId || u.UserId == regularId)
                .Select(u => u.UserId)
                .ToListAsync();
            syncedPlayers.Should().BeEquivalentTo([newcomerId, regularId],
                "the unknown players are synced while handing out the roles");
        }
        finally
        {
            File.Delete(configFilePath);
        }
    }

    /// <summary>
    /// A difficulty whose new challenge could not be created keeps the one it already has, so its
    /// players are still rewarded on the next run; the other difficulties move on regardless.
    /// </summary>
    [Fact]
    public async Task DailyChallenge_KeepsTheActiveChallenge_OfTheDifficultyThatCouldNotBeRecreated()
    {
        var hard = $"Hard-{Guid.NewGuid():N}"[..16];
        var easy = $"Easy-{Guid.NewGuid():N}"[..16];
        var hardToken = $"hard-{Guid.NewGuid():N}"[..16];
        var easyToken = $"easy-{Guid.NewGuid():N}"[..16];

        await SeedChallengeLinksAsync((hard, 2, hardToken), (easy, 1, easyToken));

        var configFilePath = await WriteChallengeConfigAsync((hard, 2), (easy, 1));
        try
        {
            var (host, client) = CreateChallengeHost(configFilePath);
            using var _ = host;
            client.CreateChallengeAsync(
                    Arg.Is<PostChallengeRequestDto>(r => r!.Map == $"map-{hard}"), Arg.Any<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("GeoGuessr is down"));

            await host.SendAsync(new DailyChallengeCommand());

            var links = await ReadLinksAsync(hard, easy);
            links.Should().ContainSingle(l => l.Difficulty == hard)
                .Which.ChallengeId.Should().Be(hardToken, "the challenge that could not be replaced stays active");
            links.Should().ContainSingle(l => l.Difficulty == easy)
                .Which.ChallengeId.Should().Be("new-token", "the other difficulty moves on");

            await host.Mock<IDiscordMessageAccess>()
                .Received()
                .SendMessageAsync(Arg.Is<string>(m => m!.Contains($"{hard}: ERROR")), TextChannelId, Arg.Any<CancellationToken>());
        }
        finally
        {
            File.Delete(configFilePath);
        }
    }

    /// <summary>
    /// If no challenge at all could be created, the active ones are left untouched so the next run
    /// picks up exactly where this one stopped — and the players are told instead of being left with
    /// a silent gap.
    /// </summary>
    [Fact]
    public async Task DailyChallenge_KeepsAllActiveChallenges_WhenNoneCanBeRecreated()
    {
        var hard = $"Hard-{Guid.NewGuid():N}"[..16];
        var easy = $"Easy-{Guid.NewGuid():N}"[..16];
        var hardToken = $"hard-{Guid.NewGuid():N}"[..16];
        var easyToken = $"easy-{Guid.NewGuid():N}"[..16];

        await SeedChallengeLinksAsync((hard, 2, hardToken), (easy, 1, easyToken));

        var configFilePath = await WriteChallengeConfigAsync((hard, 2), (easy, 1));
        try
        {
            var (host, client) = CreateChallengeHost(configFilePath);
            using var _ = host;
            client.CreateChallengeAsync(Arg.Any<PostChallengeRequestDto>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("GeoGuessr is down"));

            await host.SendAsync(new DailyChallengeCommand());

            var links = await ReadLinksAsync(hard, easy);
            links.Select(l => l.ChallengeId).Should().BeEquivalentTo([hardToken, easyToken],
                "nothing was created, so the challenges that are running stay the active ones");

            await host.Mock<IDiscordMessageAccess>()
                .Received()
                .SendMessageAsync(
                    Arg.Is<string>(m => m!.Contains("No new challenges could be created")),
                    TextChannelId,
                    Arg.Any<CancellationToken>());
        }
        finally
        {
            File.Delete(configFilePath);
        }
    }

    /// <summary>
    /// A player that already earned a role in a higher-priority challenge must not earn a second one
    /// for a lower-priority challenge — they are skipped there and the players behind them move up.
    /// </summary>
    [Fact]
    public async Task DistributeRoles_SkipsPlayersThatAlreadyEarnedAHigherPriorityRole()
    {
        var winnerId = NewUserId();
        var winnerDiscordId = NewDiscordId();
        var runnerUpId = NewUserId();
        var runnerUpDiscordId = NewDiscordId();
        var thirdId = NewUserId();
        var thirdDiscordId = NewDiscordId();

        await SeedLinkedUsersAsync(
            (winnerId, winnerDiscordId), (runnerUpId, runnerUpDiscordId), (thirdId, thirdDiscordId));

        using var host = CreateHost();
        ArrangeUserProfiles(host);

        // The winner tops the high-priority challenge and yesterday's easy one; because they already
        // earned the winner role, the easy leaderboard shifts up by one.
        var results = new List<ClubChallengeResult>
        {
            new("Hard", 2, [Player(winnerId)]),
            new("Easy", 1, [Player(winnerId), Player(runnerUpId), Player(thirdId)]),
        };

        await host.SendAsync(new DistributeDailyChallengeRolesCommand(results));

        var roles = host.Mock<IDiscordServerRolesAccess>();
        await roles.Received().AddRoleToMembersByUserIdsAsync(
            Arg.Is<IEnumerable<ulong>>(ids => ids!.SequenceEqual(new[] { winnerDiscordId, runnerUpDiscordId })),
            FirstRoleId,
            Arg.Any<CancellationToken>());
        await roles.Received().AddRoleToMembersByUserIdsAsync(
            Arg.Is<IEnumerable<ulong>>(ids => ids!.SequenceEqual(new[] { thirdDiscordId })),
            SecondRoleId,
            Arg.Any<CancellationToken>());
        await roles.Received().AddRoleToMembersByUserIdsAsync(
            Arg.Is<IEnumerable<ulong>>(ids => !ids!.Any()),
            ThirdRoleId,
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A host wired for the full <see cref="DailyChallengeCommand"/>: a main club whose GeoGuessr
    /// client creates challenges and serves empty leaderboards, plus profile lookups for unknown
    /// players. Tests override the returned client per challenge to fail or fill in a leaderboard.
    /// </summary>
    private (MediatorTestHost Host, IGeoGuessrClient Client) CreateChallengeHost(string configFilePath)
    {
        var mainClubId = Guid.NewGuid();
        var host = CreateHost(services => services.AddSingleton(Options.Create(new GeoGuessrConfiguration
        {
            SyncSchedule = "0 0 0 * * ?",
            ActivityNcfaToken = "x",
            MissionsNcfaToken = "x",
            UserProfileNcfaToken = "x",
            Clubs = [new GeoGuessrClubEntry { ClubId = mainClubId, NcfaToken = "x", IsMain = true }],
        })), configFilePath);
        ArrangeUserProfiles(host);

        var client = Substitute.For<IGeoGuessrClient>();
        client.CreateChallengeAsync(Arg.Any<PostChallengeRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(new PostChallengeResponseDto { Token = "new-token" });
        // The links table is shared with the other integration tests, so leftover challenges of
        // theirs may be read as well — those simply have no results.
        client.ReadHighscoresAsync(Arg.Any<string>(), Arg.Any<ReadHighscoresQueryParams>(), Arg.Any<CancellationToken>())
            .Returns(Highscores());
        host.Mock<IGeoGuessrClientFactory>().CreateClient(mainClubId).Returns(client);

        return (host, client);
    }

    private async Task<List<ClubChallengeLink>> ReadLinksAsync(params string[] difficulties)
    {
        await using var read = fixture.CreateDbContext();
        return await read.LatestClubChallengeLinks.AsNoTracking()
            .Where(l => difficulties.Contains(l.Difficulty))
            .ToListAsync();
    }

    private MediatorTestHost CreateHost(Action<IServiceCollection>? configure = null, string configurationFilePath = "challenges.json") =>
        new(fixture.ConnectionString, services =>
        {
            services.AddSingleton(Options.Create(new DailyChallengesConfiguration
            {
                Schedule = "0 0 0 * * ?",
                TextChannelId = TextChannelId,
                ConfigurationFilePath = configurationFilePath,
                FirstRoleId = FirstRoleId,
                SecondRoleId = SecondRoleId,
                ThirdRoleId = ThirdRoleId,
            }));
            configure?.Invoke(services);
        });

    private async Task SeedLinkedUsersAsync(params (string UserId, ulong DiscordId)[] users)
    {
        await using var seed = fixture.CreateDbContext();
        foreach (var (userId, discordId) in users)
        {
            seed.Add(GeoGuessrUser.Create(userId, Nickname(userId), discordId));
        }

        await seed.SaveChangesAsync();
    }

    private async Task SeedChallengeLinksAsync(params (string Difficulty, int RolePriority, string ChallengeId)[] links)
    {
        await using var seed = fixture.CreateDbContext();
        foreach (var (difficulty, rolePriority, challengeId) in links)
        {
            seed.Add(ClubChallengeLink.Create(difficulty, rolePriority, challengeId));
        }

        await seed.SaveChangesAsync();
    }

    private static async Task<string> WriteChallengeConfigAsync(params (string Difficulty, int RolePriority)[] difficulties)
    {
        var config = difficulties
            .Select(d => new ClubChallengeConfigurationDifficulty(d.Difficulty)
            {
                RolePriority = d.RolePriority,
                Entries =
                [
                    new ClubChallengeConfigurationDifficultyEntry(
                        Description: "A world map", MapId: $"map-{d.Difficulty}", ForbidMoving: true,
                        ForbidRotating: false, ForbidZooming: false, TimeLimit: 60),
                ],
            })
            .ToList();

        var path = Path.Combine(Path.GetTempPath(), $"challenges-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(config));
        return path;
    }

    private static ClubChallengeResultPlayer Player(string userId) =>
        new(userId, Nickname(userId), "5000 points", "1000m");

    private static ChallengeResultHighscoresDto Highscores(params string[] userIds) => new()
    {
        Items = userIds.Select(id => new ChallengeResultItemDto
        {
            Game = new ChallengeResultGameDto
            {
                Player = new ChallengeResultPlayerDto
                {
                    Id = id,
                    Nick = Nickname(id),
                    TotalScore = new ChallengeResultPlayerScoreDto { Amount = "5000", Unit = "points" },
                    TotalDistance = new ChallengeResultPlayerDistanceDto
                    {
                        Meters = new ChallengeResultPlayerDistanceMetersDto { Amount = "1000", Unit = "km" },
                    },
                },
            },
        }).ToList(),
    };

    /// <summary>Any player the bot has not seen before is resolved through the GeoGuessr profile API.</summary>
    private static void ArrangeUserProfiles(MediatorTestHost host)
    {
        var client = Substitute.For<IGeoGuessrClient>();
        client.ReadUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => NewUserDto(callInfo.ArgAt<string>(0)));
        host.Mock<IGeoGuessrClientFactory>().CreateUserProfileClient().Returns(client);
    }

    private static string Nickname(string userId) => $"nick-{userId}";

    private static UserDto NewUserDto(string userId) => new()
    {
        Id = userId,
        Nick = Nickname(userId),
        Created = DateTimeOffset.UtcNow,
        IsProUser = false,
        Type = "user",
        IsVerified = false,
        CustomImage = "",
        FullBodyPin = "",
        BorderUrl = "",
        Color = 0,
        Url = $"/user/{userId}",
        CountryCode = "us",
        Competitive = new UserCompetitiveDto { Elo = 1000, Rating = 1000, LastRatingChange = 0 },
        IsBanned = false,
        ChatBan = false,
    };
}
