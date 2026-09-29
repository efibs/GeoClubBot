using Configuration;
using Entities;
using FluentAssertions;
using GeoClubBot.DependencyInjection;
using GeoClubBot.Tests.TestBuilders;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using Xunit;

namespace GeoClubBot.Tests.Api;

public sealed class CachingClubMissionBoardReaderTests
{
    private static readonly Guid ClubId = Guid.NewGuid();

    private readonly IGeoGuessrClientFactory _clientFactory = Substitute.For<IGeoGuessrClientFactory>();
    private readonly IGeoGuessrClient _client = Substitute.For<IGeoGuessrClient>();
    private readonly IClubMemberRepository _clubMembers = Substitute.For<IClubMemberRepository>();
    private readonly ILogger<CachingClubMissionBoardReader> _logger = Substitute.For<ILogger<CachingClubMissionBoardReader>>();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public CachingClubMissionBoardReaderTests()
    {
        _clientFactory.CreateClient(ClubId).Returns(_client);
        _logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        _clubMembers.ReadClubMembersByUserIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, ClubMember>());
    }

    private CachingClubMissionBoardReader CreateReader() => new(
        _clientFactory, _clubMembers, _cache,
        Options.Create(new GeoGuessrConfiguration
        {
            SyncSchedule = "x",
            ActivityNcfaToken = "x",
            UserProfileNcfaToken = "x",
            Clubs = [new GeoGuessrClubEntry { ClubId = ClubId, NcfaToken = "x", IsMain = true }]
        }),
        _logger);

    private static ClubMissionBoardSnapshotDto Snapshot(string? claimedBy = null) => new()
    {
        PeriodStart = MissionBoards.PeriodStart,
        PeriodEnd = MissionBoards.PeriodStart.AddDays(7),
        CurrentBoardNumber = 1,
        AllBoardsCleared = false,
        Boards =
        [
            new ClubMissionBoardDto
            {
                Number = 1, Size = 1,
                Tiles = [new ClubMissionTileDto { MissionId = Guid.NewGuid(), TemplateId = "t", ClaimedBy = claimedBy, ClaimedAt = claimedBy is null ? null : DateTimeOffset.UtcNow }]
            }
        ]
    };

    [Fact]
    public async Task ReadCurrent_ReadsWithTheClubsOwnClient_AndCachesTheBoard()
    {
        _client.ReadClubMissionBoardAsync(Arg.Any<CancellationToken>()).Returns(Snapshot());

        var first = await CreateReader().ReadCurrentAsync(ClubId);
        var second = await CreateReader().ReadCurrentAsync(ClubId);

        first.Should().NotBeNull();
        second.Should().BeSameAs(first);
        await _client.Received(1).ReadClubMissionBoardAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadCurrent_ReturnsNull_WhenGeoGuessrFails()
    {
        _client.ReadClubMissionBoardAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("down"));

        var board = await CreateReader().ReadCurrentAsync(ClubId);

        board.Should().BeNull();
    }

    [Fact]
    public async Task ReadCurrent_Warns_WhenNoClaimerIsAMemberOfTheClub()
    {
        // The token belongs to someone in another club: the board is of the wrong club.
        _client.ReadClubMissionBoardAsync(Arg.Any<CancellationToken>()).Returns(Snapshot(claimedBy: "stranger"));

        await CreateReader().ReadCurrentAsync(ClubId);

        _logger.Received().Log(LogLevel.Warning, Arg.Any<EventId>(), Arg.Any<object>(), Arg.Any<Exception?>(), Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task ReadCurrent_DoesNotWarn_WhenAClaimerIsAMember()
    {
        var member = new ClubMemberBuilder().WithUserId("member").InClub(ClubId).Build();
        _clubMembers.ReadClubMembersByUserIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, ClubMember> { ["member"] = member });
        _client.ReadClubMissionBoardAsync(Arg.Any<CancellationToken>()).Returns(Snapshot(claimedBy: "member"));

        await CreateReader().ReadCurrentAsync(ClubId);

        _logger.DidNotReceive().Log(LogLevel.Warning, Arg.Any<EventId>(), Arg.Any<object>(), Arg.Any<Exception?>(), Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task ReadPrevious_ReturnsNull_InTheFirstWeek()
    {
        _client.ReadPreviousClubMissionBoardAsync(Arg.Any<CancellationToken>()).Returns((ClubMissionBoardSnapshotDto?)null);

        (await CreateReader().ReadPreviousAsync(ClubId)).Should().BeNull();
    }
}
