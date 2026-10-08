using Configuration;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.UseCases.Website;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.Website;

public sealed class GetWebsiteStatsHandlerTests
{
    private static readonly Guid MainClubId = Guid.NewGuid();
    private static readonly Guid SecondClubId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 9, 12, 40, TimeSpan.Zero);

    private readonly IGeoGuessrClubReader _clubReader = Substitute.For<IGeoGuessrClubReader>();
    private readonly IDiscordOnlineCountReader _onlineCountReader = Substitute.For<IDiscordOnlineCountReader>();
    private readonly ILogger<GetWebsiteStatsHandler> _logger = Substitute.For<ILogger<GetWebsiteStatsHandler>>();

    public GetWebsiteStatsHandlerTests()
    {
        _logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        ArrangeClub(MainClubId, rank: 3, level: 36, members: 30, xp: 425770, totalClubs: 29168, readAt: Now.AddMinutes(-12));
        ArrangeClub(SecondClubId, rank: 221, level: 20, members: 25, xp: 123400, totalClubs: 29169, readAt: Now.AddMinutes(-5));
        _onlineCountReader.ReadOnlineCountAsync(Arg.Any<CancellationToken>()).Returns(new DiscordOnlineCount(18, Now));
    }

    private void ArrangeClub(Guid clubId, int rank, int level, int members, int xp, int totalClubs, DateTimeOffset readAt) =>
        _clubReader.ReadClubAsync(clubId, Arg.Any<CancellationToken>())
            .Returns(new GeoGuessrClubSnapshot(clubId, level, members, xp, rank, totalClubs, readAt));

    private GetWebsiteStatsHandler CreateHandler(bool enabled = true) => new(
        _clubReader,
        _onlineCountReader,
        Options.Create(new WebsiteConfiguration { Enabled = enabled, SecondClubId = SecondClubId }),
        new GeoGuessrConfigurationBuilder()
            .WithClub(MainClubId)
            .WithClub(SecondClubId, isMain: false)
            .BuildOptions(),
        _logger);

    private async Task<WebsiteStats> HandleAsync()
    {
        var result = await CreateHandler().Handle(new GetWebsiteStatsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenTheWebsiteIsDisabled()
    {
        var result = await CreateHandler(enabled: false).Handle(new GetWebsiteStatsQuery(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("website.disabled");
        await _clubReader.DidNotReceiveWithAnyArgs().ReadClubAsync(default);
        await _onlineCountReader.DidNotReceiveWithAnyArgs().ReadOnlineCountAsync();
    }

    [Fact]
    public async Task Handle_ReturnsBothClubsAndTheOnlineCount()
    {
        var stats = await HandleAsync();

        stats.Should().Be(new WebsiteStats(
            new WebsiteGeoGuessrStats(
                Now.AddMinutes(-12),
                29168,
                new WebsiteClubStats(Rank: 3, Level: 36, Members: 30, Xp: 425770),
                new WebsiteClubStats(Rank: 221, Level: 20, Members: 25, Xp: 123400)),
            new WebsiteDiscordStats(Now, 18)));
    }

    [Fact]
    public async Task Handle_DatesTheGeoGuessrNumbers_ByTheOlderRead()
    {
        ArrangeClub(MainClubId, rank: 3, level: 36, members: 30, xp: 425770, totalClubs: 29168, readAt: Now.AddMinutes(-1));
        ArrangeClub(SecondClubId, rank: 221, level: 20, members: 25, xp: 123400, totalClubs: 29168, readAt: Now.AddMinutes(-20));

        var stats = await HandleAsync();

        stats.GeoGuessr!.UpdatedAt.Should().Be(Now.AddMinutes(-20));
    }

    [Fact]
    public async Task Handle_TakesTheTotalClubs_FromTheMainClub()
    {
        var stats = await HandleAsync();

        stats.GeoGuessr!.TotalClubs.Should().Be(29168);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_LeavesOutTheGeoGuessrNumbers_WhenAClubCannotBeRead(bool mainFails)
    {
        _clubReader.ReadClubAsync(mainFails ? MainClubId : SecondClubId, Arg.Any<CancellationToken>())
            .Returns((GeoGuessrClubSnapshot?)null);

        var stats = await HandleAsync();

        stats.GeoGuessr.Should().BeNull();
        stats.Discord.Should().Be(new WebsiteDiscordStats(Now, 18));
    }

    [Fact]
    public async Task Handle_LeavesOutTheOnlineCount_WhenDiscordCannotBeRead()
    {
        _onlineCountReader.ReadOnlineCountAsync(Arg.Any<CancellationToken>()).Returns((DiscordOnlineCount?)null);

        var stats = await HandleAsync();

        stats.Discord.Should().BeNull();
        stats.GeoGuessr.Should().NotBeNull();
    }

    [Theory]
    [InlineData(0, 221, 29168)]
    [InlineData(3, 0, 29168)]
    [InlineData(3, 221, 0)]
    public async Task Handle_LeavesOutTheGeoGuessrNumbersAndWarns_WhenTheyBreakTheWebsitesSchema(
        int mainRank, int secondRank, int totalClubs)
    {
        ArrangeClub(MainClubId, mainRank, level: 36, members: 30, xp: 425770, totalClubs, readAt: Now);
        ArrangeClub(SecondClubId, secondRank, level: 20, members: 25, xp: 123400, totalClubs, readAt: Now);

        var stats = await HandleAsync();

        stats.GeoGuessr.Should().BeNull();
        stats.Discord.Should().NotBeNull();
        _logger.Received().Log(LogLevel.Warning, Arg.Any<EventId>(), Arg.Any<object>(), Arg.Any<Exception?>(), Arg.Any<Func<object, Exception?, string>>());
    }
}
