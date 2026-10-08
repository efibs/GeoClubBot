using Configuration;
using FluentAssertions;
using GeoClubBot.DependencyInjection;
using GeoClubBot.Tests.TestBuilders;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UseCases.OutputPorts.GeoGuessr;
using Xunit;

namespace GeoClubBot.Tests.Api;

public sealed class CachingGeoGuessrClubReaderTests
{
    private static readonly Guid ClubId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 9, 0, 12, TimeSpan.Zero);

    private readonly IGeoGuessrClientFactory _clientFactory = Substitute.For<IGeoGuessrClientFactory>();
    private readonly IGeoGuessrClient _client = Substitute.For<IGeoGuessrClient>();
    private readonly ILogger<CachingGeoGuessrClubReader> _logger = Substitute.For<ILogger<CachingGeoGuessrClubReader>>();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly FixedTimeProvider _clock = new(Now);

    public CachingGeoGuessrClubReaderTests()
    {
        _clientFactory.CreateClient(ClubId).Returns(_client);
        _logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
    }

    private CachingGeoGuessrClubReader CreateReader() => new(
        _clientFactory, _cache,
        Options.Create(new GeoGuessrConfiguration
        {
            SyncSchedule = "x",
            ActivityNcfaToken = "x",
            UserProfileNcfaToken = "x",
            Clubs = [new GeoGuessrClubEntry { ClubId = ClubId, NcfaToken = "x", IsMain = true }]
        }),
        _clock,
        _logger);

    private static ClubDto Club() => new()
    {
        ClubId = ClubId,
        Name = "Dragon",
        Members = [],
        JoinRule = 0,
        Tag = "DRGN",
        Description = "",
        CreatedAt = DateTimeOffset.UnixEpoch,
        Language = "en",
        MemberCount = 30,
        MaxMemberCount = 50,
        Level = 36,
        // Deliberately different from Stats.TotalXp: the website shows the stats' total.
        Xp = 1,
        Labels = [],
        Stats = new ClubStatsDto
        {
            ClubId = ClubId,
            TotalXp = 425770,
            ChangePercentXp = 0,
            TotalGamesPlayed = 0,
            ChangePercentGamesPlayed = 0,
            TotalWins = 0,
            ChangePercentWins = 0,
            TotalPerfectGuesses = 0,
            ChangePercentPerfectGuesses = 0,
            GlobalXpRank = 3,
            TotalClubs = 29168,
            AverageDivision = new ClubAverageDivisionDto { Number = 1, Name = "Gold", Tier = 1 }
        },
        BackgroundUrl = ""
    };

    [Fact]
    public async Task ReadClub_ReadsWithTheClubsOwnClient_AndCopiesTheFigures()
    {
        _client.ReadClubAsync(ClubId, Arg.Any<CancellationToken>()).Returns(Club());

        var club = await CreateReader().ReadClubAsync(ClubId);

        club.Should().Be(new GeoGuessrClubSnapshot(
            ClubId, Level: 36, MemberCount: 30, TotalXp: 425770, GlobalXpRank: 3, TotalClubs: 29168, ReadAt: Now));
    }

    [Fact]
    public async Task ReadClub_CachesTheClub_WithTheTimeItWasRead()
    {
        _client.ReadClubAsync(ClubId, Arg.Any<CancellationToken>()).Returns(Club());

        var first = await CreateReader().ReadClubAsync(ClubId);
        _clock.Now = Now.AddMinutes(10);
        var second = await CreateReader().ReadClubAsync(ClubId);

        second.Should().BeSameAs(first);
        second!.ReadAt.Should().Be(Now);
        await _client.Received(1).ReadClubAsync(ClubId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadClub_ReturnsNullAndWarns_WhenGeoGuessrFails()
    {
        _client.ReadClubAsync(ClubId, Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("down"));

        var club = await CreateReader().ReadClubAsync(ClubId);

        club.Should().BeNull();
        _logger.Received().Log(LogLevel.Warning, Arg.Any<EventId>(), Arg.Any<object>(), Arg.Any<Exception?>(), Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task ReadClub_DoesNotCacheAFailure()
    {
        _client.ReadClubAsync(ClubId, Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new HttpRequestException("down"),
                _ => Club());

        var first = await CreateReader().ReadClubAsync(ClubId);
        var second = await CreateReader().ReadClubAsync(ClubId);

        first.Should().BeNull();
        second.Should().NotBeNull();
        await _client.Received(2).ReadClubAsync(ClubId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadClub_ReturnsNull_WhenTheRequestTimesOut()
    {
        // HttpClient reports its own timeout as a TaskCanceledException, though nobody cancelled.
        _client.ReadClubAsync(ClubId, Arg.Any<CancellationToken>()).ThrowsAsync(new TaskCanceledException("timeout"));

        var club = await CreateReader().ReadClubAsync(ClubId);

        club.Should().BeNull();
    }

    [Fact]
    public async Task ReadClub_PassesOnTheCallersCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        _client.ReadClubAsync(ClubId, Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException(cancellation.Token));

        var read = () => CreateReader().ReadClubAsync(ClubId, cancellation.Token);

        await read.Should().ThrowAsync<OperationCanceledException>();
    }
}
