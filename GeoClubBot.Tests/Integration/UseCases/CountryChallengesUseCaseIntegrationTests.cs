using Configuration;
using Entities;
using FluentAssertions;
using Infrastructure.OutputAdapters.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.CountryChallenges;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.UseCases.CountryChallenges;
using UseCases.UseCases.CountryChallenges.Configuration;
using Utilities;
using Xunit;
using static GeoClubBot.Tests.TestBuilders.CountryChallengeFiles;

namespace GeoClubBot.Tests.Integration.UseCases;

/// <summary>
/// Runs the country challenges through the real MediatR pipeline and Postgres, covering what only a real
/// database can show: that a run stores what it announces and a second run on the same day finds it,
/// that results evaluated a day later land as points in the season, that an import replaces the last
/// one, and that the database itself refuses the same challenge twice on one day.
///
/// The tables are shared with every other test using the container, so each test works on its own
/// challenge names, season, players and — because the evaluation and the leaderboard look up by date —
/// on its own day far in the future.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CountryChallengesUseCaseIntegrationTests(PostgresFixture fixture)
{
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..12];
    private readonly DateOnly _day = new DateOnly(2100, 1, 1).AddDays(Random.Shared.Next(0, 36_000));

    [Fact]
    public async Task Run_StoresWhatItAnnounces_AndASecondRunTheSameDayChangesNothing()
    {
        var name = $"Mongolia {_suffix}";
        var (host, client) = CreateHost(File(Fixed(name, _day.DayOfWeek, Country("Mongolia", "MN"))) with
        {
            Leaderboard = new LeaderboardSection { Enabled = false }
        });
        using var _ = host;

        var first = await host.SendAsync(new RunCountryChallengesCommand(_day));
        var second = await host.SendAsync(new RunCountryChallengesCommand(_day));

        first.Value.Announcement!.Announced.Should().Equal($"{name}: Mongolia");
        second.Value.Announcement!.AlreadyPosted.Should().Equal(name);
        await client.Received(1).CreateChallengeAsync(Arg.Any<PostChallengeRequestDto>(), Arg.Any<CancellationToken>());

        await using var read = fixture.CreateDbContext();
        var post = await read.CountryChallengePosts.AsNoTracking().SingleAsync(p => p.ChallengeName == name);
        post.Should().BeEquivalentTo(new
        {
            Date = _day,
            Country = "Mongolia",
            CountryCode = "MN",
            ChallengeId = "token-1",
            ChannelId,
            ResultsDueOn = (DateOnly?)_day.AddDays(1),
            EvaluatedAt = (DateTimeOffset?)null
        });
    }

    [Fact]
    public async Task Run_StoresOneRowPerPick_AndASecondRunCreatesNoMore()
    {
        var name = $"Middleweight {_suffix}";
        var (host, client) = CreateHost(File(Pool(name, _day.DayOfWeek, Country("Peru"), Country("Chile"), Country("Japan")) with
        {
            Picks = 2
        }) with
        {
            Leaderboard = new LeaderboardSection { Enabled = false }
        });
        using var _ = host;

        await host.SendAsync(new RunCountryChallengesCommand(_day));
        var second = await host.SendAsync(new RunCountryChallengesCommand(_day));

        second.Value.Announcement!.AlreadyPosted.Should().Equal(name);
        await client.Received(2).CreateChallengeAsync(Arg.Any<PostChallengeRequestDto>(), Arg.Any<CancellationToken>());

        await using var read = fixture.CreateDbContext();
        var countries = await read.CountryChallengePosts.AsNoTracking()
            .Where(p => p.ChallengeName == name && p.Date == _day)
            .Select(p => p.Country)
            .ToListAsync();
        countries.Should().HaveCount(2).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Run_EvaluatesTheResultsTheNextDay_AndPostsTheLeaderboardWithThem()
    {
        var name = $"Argentina {_suffix}";
        var season = $"Season {_suffix}";
        var nextDay = _day.AddDays(1);
        var (host, client) = CreateHost(File(Fixed(name, _day.DayOfWeek, Country("Argentina"))) with
        {
            Leaderboard = new LeaderboardSection { Season = season, Days = [nextDay.DayOfWeek] }
        });
        using var _ = host;
        var (anna, bert) = ($"anna-{_suffix}", $"bert-{_suffix}");
        client.ReadHighscoresAsync("token-1", Arg.Any<ReadHighscoresQueryParams>(), Arg.Any<CancellationToken>())
            .Returns(Highscores(anna, bert));

        await host.SendAsync(new RunCountryChallengesCommand(_day));
        var report = (await host.SendAsync(new RunCountryChallengesCommand(nextDay))).Value;

        report.Evaluation!.Evaluated.Should().Equal($"{name}: Argentina ({_day:yyyy-MM-dd})");
        report.Leaderboard!.Status.Should().Be(CountryChallengeLeaderboardStatus.Posted);

        await using var read = fixture.CreateDbContext();
        var awards = await read.CountryChallengePointAwards.AsNoTracking().Where(a => a.Season == season).ToListAsync();
        awards.Select(a => (a.UserId, a.Points, a.Place)).Should().BeEquivalentTo([(anna, 3, (int?)1), (bert, 2, (int?)2)]);
        (await read.CountryChallengePosts.AsNoTracking().SingleAsync(p => p.ChallengeName == name))
            .EvaluatedAt.Should().NotBeNull();
        (await read.CountryChallengeLeaderboardPosts.AsNoTracking().SingleAsync(p => p.Date == nextDay))
            .Season.Should().Be(season);

        // A third run must neither count the points again nor repost the leaderboard.
        var again = (await host.SendAsync(new RunCountryChallengesCommand(nextDay))).Value;
        again.Evaluation!.Evaluated.Should().BeEmpty();
        again.Leaderboard!.Status.Should().Be(CountryChallengeLeaderboardStatus.AlreadyPosted);
    }

    [Fact]
    public async Task Import_ReplacesThePreviousImportOfTheSeason_ButKeepsChallengePoints()
    {
        var season = $"Season {_suffix}";
        var (host, _) = CreateHost(File(Fixed($"Chile {_suffix}", DayOfWeek.Monday, Country("Chile"))) with
        {
            Leaderboard = new LeaderboardSection { Season = season }
        });
        using var __ = host;
        var (fibs, anna) = ($"Fibs{_suffix}", $"Anna{_suffix}");
        await SeedAsync(
            GeoGuessrUser.Create(PlayerId("f"), fibs),
            GeoGuessrUser.Create(PlayerId("a"), anna),
            CountryChallengePointAward.Imported("other season", PlayerId("f"), fibs, 50, DateTimeOffset.UtcNow));

        await host.SendAsync(new ImportCountryChallengeStandingsCommand($"{fibs} 12\n{anna} 5"));
        await SeedAsync(CountryChallengePointAward.ForPlacement(season, await SeedPostAsync(), 1, PlayerId("a"), anna, 3, DateTimeOffset.UtcNow));
        var result = await host.SendAsync(new ImportCountryChallengeStandingsCommand($"{fibs.ToUpperInvariant()} 10"));

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        result.Value.Top.Select(s => (s.Nickname, s.Points)).Should().Equal((fibs, 10), (anna, 3));

        await using var read = fixture.CreateDbContext();
        var imported = await read.CountryChallengePointAwards.AsNoTracking()
            .Where(a => a.Season == season && a.Source == CountryChallengePointSource.Import)
            .ToListAsync();
        imported.Should().ContainSingle().Which.Points.Should().Be(10);
    }

    [Fact]
    public async Task TheDatabase_RefusesTheSameCountryOfAChallengeTwiceOnOneDay_ButNotASecondCountry()
    {
        var name = $"Twice {_suffix}";
        await SeedAsync(PostWithoutResults(name));

        await SeedAsync(PostWithoutResults(name, "Chile"));
        var again = () => SeedAsync(PostWithoutResults(name));

        await again.Should().ThrowAsync<DbUpdateException>();
    }

    /// <summary>
    /// What <c>results-now</c> and the mock's restart read: every challenge still waiting for results,
    /// due or not. Only this test's own rows are checked — other tests' pending rows share the table.
    /// </summary>
    [Fact]
    public async Task ReadPendingPosts_ReturnsEveryChallengeWaitingForResults_DueOrNot()
    {
        var notYetDue = Post($"Future {_suffix}", _day, resultsDueOn: _day.AddDays(30));
        var evaluated = Post($"Done {_suffix}", _day);
        evaluated.MarkEvaluated(DateTimeOffset.UtcNow);
        await SeedAsync(notYetDue, evaluated, PostWithoutResults($"None {_suffix}"));

        await using var db = fixture.CreateDbContext();
        var pending = await new EfCountryChallengeRepository(db).ReadPendingPostsAsync();

        pending.Where(p => p.ChallengeName.EndsWith(_suffix)).Select(p => p.ChallengeName)
            .Should().Equal($"Future {_suffix}");
    }

    [Fact]
    public async Task ReadUsersByNickname_MatchesTheWholeNicknameIgnoringCase()
    {
        var nickname = $"CaseTest{_suffix}";
        await SeedAsync(GeoGuessrUser.Create(PlayerId("c"), nickname), GeoGuessrUser.Create(PlayerId("d"), nickname + "x"));

        await using var db = fixture.CreateDbContext();
        var matches = await new EfGeoGuessrUserRepository(db).ReadUsersByNicknameAsync(nickname.ToLowerInvariant());

        matches.Should().ContainSingle().Which.UserId.Should().Be(PlayerId("c"));
    }

    /// <summary>A 24-character GeoGuessr-style user id, unique to this test.</summary>
    private string PlayerId(string prefix) => $"{prefix}{_suffix}".PadRight(24, '0')[..24];

    private (MediatorTestHost Host, IGeoGuessrClient Client) CreateHost(CountryChallengesFile file)
    {
        var mainClubId = Guid.NewGuid();
        var host = new MediatorTestHost(fixture.ConnectionString, services =>
        {
            services.AddSingleton(Options.Create(new CountryChallengesConfiguration
            {
                Enabled = true,
                Schedule = "0 0 17 ? * * *",
                ConfigurationFilePath = "unused-the-source-is-faked.json"
            }));
            services.AddSingleton(Options.Create(new GeoGuessrConfiguration
            {
                SyncSchedule = "0 0 0 * * ?",
                ActivityNcfaToken = "x",
                MissionsNcfaToken = "x",
                UserProfileNcfaToken = "x",
                Clubs = [new GeoGuessrClubEntry { ClubId = mainClubId, NcfaToken = "x", IsMain = true }]
            }));
        });

        host.Mock<ICountryChallengeConfigurationSource>().ReadAsync(Arg.Any<CancellationToken>())
            .Returns(Result<CountryChallengesFile>.Success(file));

        var tokens = 0;
        var client = Substitute.For<IGeoGuessrClient>();
        client.CreateChallengeAsync(Arg.Any<PostChallengeRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => new PostChallengeResponseDto { Token = $"token-{++tokens}" });
        client.ReadHighscoresAsync(Arg.Any<string>(), Arg.Any<ReadHighscoresQueryParams>(), Arg.Any<CancellationToken>())
            .Returns(Highscores());
        host.Mock<IGeoGuessrClientFactory>().CreateClient(mainClubId).Returns(client);

        return (host, client);
    }

    private async Task SeedAsync(params object[] entities)
    {
        await using var seed = fixture.CreateDbContext();
        seed.AddRange(entities);
        await seed.SaveChangesAsync();
    }

    /// <summary>A post for points to point at.</summary>
    private async Task<int> SeedPostAsync()
    {
        var post = PostWithoutResults($"Seeded {_suffix}");
        await SeedAsync(post);
        return post.Id;
    }

    /// <summary>
    /// A post that is never due, so a concurrent test whose day happens to be close cannot evaluate it.
    /// </summary>
    private CountryChallengePost PostWithoutResults(string name, string country = "Mongolia") =>
        CountryChallengePost.Create(
            name, _day, country, null, "map", null, 60, false, false, false, "token", ChannelId,
            DateTimeOffset.UtcNow, resultsDueOn: null);
}
