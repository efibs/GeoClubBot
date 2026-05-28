using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.UseCases.ClubMemberActivity;
using UseCases.UseCases.ClubMembers;
using UseCases.UseCases.Strikes;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.ClubMemberActivity;

public sealed class CheckGeoGuessrPlayerActivityHandlerTests
{
    private static readonly Guid ClubId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly IGeoGuessrClientFactory _clientFactory = Substitute.For<IGeoGuessrClientFactory>();
    private readonly IGeoGuessrClient _client = Substitute.For<IGeoGuessrClient>();
    private readonly IStrikesRepository _strikes = Substitute.For<IStrikesRepository>();
    private readonly IExcusesRepository _excuses = Substitute.For<IExcusesRepository>();
    private readonly IClubRepository _clubs = Substitute.For<IClubRepository>();
    private readonly IHistoryRepository _history = Substitute.For<IHistoryRepository>();
    private readonly IActivityStatusMessageSender _messageSender = Substitute.For<IActivityStatusMessageSender>();
    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly ILogger<CheckGeoGuessrPlayerActivityHandler> _logger =
        Substitute.For<ILogger<CheckGeoGuessrPlayerActivityHandler>>();

    public CheckGeoGuessrPlayerActivityHandlerTests()
    {
        _clientFactory.CreateClient(Arg.Any<Guid>()).Returns(_client);
        _clubs.ReadClubByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Club.Create(ClubId, "TestClub", 5));
        _excuses.ReadExcusesAsync(Arg.Any<CancellationToken>()).Returns([]);
        _history.ReadLatestHistoryEntriesByClubIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([]);
    }

    [Fact]
    public async Task Handle_CreatesStrike_WhenMemberFallsShortOfTarget()
    {
        // Member joined 6 months ago (well past the 7-day grace period). Prior history shows
        // 1000 XP; API still says 1000 XP → delta = 0, target = 100, so a strike is created.
        var joinedAt = DateTimeOffset.UtcNow.AddMonths(-6);
        var apiResponse = new List<ClubMemberDto>
        {
            BuildApiMember("user-1", "Player1", xp: 1000, joinedAt)
        };
        _client.ReadClubMembersAsync(ClubId, Arg.Any<CancellationToken>()).Returns(apiResponse);
        _history.ReadLatestHistoryEntriesByClubIdAsync(ClubId, Arg.Any<CancellationToken>())
            .Returns(new List<ClubMemberHistoryEntry>
            {
                new HistoryEntryBuilder().WithUserId("user-1").InClub(ClubId).WithXp(1000)
                    .At(DateTimeOffset.UtcNow.AddDays(-7)).Build()
            });

        var persistedMember = new ClubMemberBuilder()
            .WithUserId("user-1").WithNickname("Player1")
            .InClub(ClubId).WithXp(1000).JoinedAt(joinedAt)
            .Build();
        _mediator
            .Send(Arg.Is<ReadOrSyncClubMemberByUserIdQuery>(q => q.UserId == "user-1"), Arg.Any<CancellationToken>())
            .Returns(Result<ClubMember>.Success(persistedMember));

        _strikes.ReadNumberOfActiveStrikesByMemberUserIdAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(0);

        var handler = CreateHandler();

        var statuses = await handler.Handle(new CheckGeoGuessrPlayerActivityCommand(ClubId), CancellationToken.None);

        statuses.Should().HaveCount(1);
        statuses[0].TargetAchieved.Should().BeFalse();
        statuses[0].NumStrikes.Should().Be(1);
        statuses[0].IsOutOfStrikes.Should().BeFalse();
        _strikes.Received(1).CreateStrike(Arg.Is<ClubMemberStrike>(s => s.UserId == "user-1"));
    }

    [Fact]
    public async Task Handle_DoesNotCreateStrike_WhenMemberMeetsTarget()
    {
        // Member earned 200 XP since last entry; target = 100 → no strike.
        var joinedAt = DateTimeOffset.UtcNow.AddMonths(-6);
        var apiResponse = new List<ClubMemberDto>
        {
            BuildApiMember("user-1", "Player1", xp: 1200, joinedAt)
        };
        _client.ReadClubMembersAsync(ClubId, Arg.Any<CancellationToken>()).Returns(apiResponse);

        // Latest historical entry says they had 1000 XP. Delta = 200.
        _history.ReadLatestHistoryEntriesByClubIdAsync(ClubId, Arg.Any<CancellationToken>())
            .Returns(new List<ClubMemberHistoryEntry>
            {
                new HistoryEntryBuilder().WithUserId("user-1").InClub(ClubId).WithXp(1000)
                    .At(DateTimeOffset.UtcNow.AddDays(-7)).Build()
            });

        var persistedMember = new ClubMemberBuilder()
            .WithUserId("user-1").WithNickname("Player1")
            .InClub(ClubId).WithXp(1200).JoinedAt(joinedAt)
            .Build();
        _mediator
            .Send(Arg.Is<ReadOrSyncClubMemberByUserIdQuery>(q => q.UserId == "user-1"), Arg.Any<CancellationToken>())
            .Returns(Result<ClubMember>.Success(persistedMember));

        _strikes.ReadNumberOfActiveStrikesByMemberUserIdAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(0);

        var handler = CreateHandler();

        var statuses = await handler.Handle(new CheckGeoGuessrPlayerActivityCommand(ClubId), CancellationToken.None);

        statuses.Should().HaveCount(1);
        statuses[0].TargetAchieved.Should().BeTrue();
        statuses[0].XpSinceLastUpdate.Should().Be(200);
        _strikes.DidNotReceive().CreateStrike(Arg.Any<ClubMemberStrike>());
    }

    [Fact]
    public async Task Handle_FlipsIsOutOfStrikes_WhenAccruedStrikesExceedMax()
    {
        // maxNumStrikes = 3; member already has 3 active strikes; this run accrues a 4th
        // (delta = 0, target = 100).
        var joinedAt = DateTimeOffset.UtcNow.AddMonths(-6);
        var apiResponse = new List<ClubMemberDto>
        {
            BuildApiMember("user-1", "Player1", xp: 1000, joinedAt)
        };
        _client.ReadClubMembersAsync(ClubId, Arg.Any<CancellationToken>()).Returns(apiResponse);
        _history.ReadLatestHistoryEntriesByClubIdAsync(ClubId, Arg.Any<CancellationToken>())
            .Returns(new List<ClubMemberHistoryEntry>
            {
                new HistoryEntryBuilder().WithUserId("user-1").InClub(ClubId).WithXp(1000)
                    .At(DateTimeOffset.UtcNow.AddDays(-7)).Build()
            });

        var persistedMember = new ClubMemberBuilder()
            .WithUserId("user-1").WithNickname("Player1")
            .InClub(ClubId).WithXp(1000).JoinedAt(joinedAt)
            .Build();
        _mediator
            .Send(Arg.Is<ReadOrSyncClubMemberByUserIdQuery>(q => q.UserId == "user-1"), Arg.Any<CancellationToken>())
            .Returns(Result<ClubMember>.Success(persistedMember));

        _strikes.ReadNumberOfActiveStrikesByMemberUserIdAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(3);

        var handler = CreateHandler();

        var statuses = await handler.Handle(new CheckGeoGuessrPlayerActivityCommand(ClubId), CancellationToken.None);

        statuses.Should().HaveCount(1);
        statuses[0].NumStrikes.Should().Be(4);
        statuses[0].IsOutOfStrikes.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_DispatchesCheckStrikeDecayCommand_BeforeSyncing()
    {
        var joinedAt = DateTimeOffset.UtcNow.AddMonths(-6);
        var apiResponse = new List<ClubMemberDto>
        {
            BuildApiMember("user-1", "Player1", xp: 1000, joinedAt)
        };
        _client.ReadClubMembersAsync(ClubId, Arg.Any<CancellationToken>()).Returns(apiResponse);

        var persistedMember = new ClubMemberBuilder()
            .WithUserId("user-1").WithNickname("Player1")
            .InClub(ClubId).WithXp(1000).JoinedAt(joinedAt).Build();
        _mediator
            .Send(Arg.Is<ReadOrSyncClubMemberByUserIdQuery>(q => q.UserId == "user-1"), Arg.Any<CancellationToken>())
            .Returns(Result<ClubMember>.Success(persistedMember));

        var handler = CreateHandler();

        await handler.Handle(new CheckGeoGuessrPlayerActivityCommand(ClubId), CancellationToken.None);

        await _mediator.Received(1).Send(Arg.Any<CheckStrikeDecayCommand>(), Arg.Any<CancellationToken>());
        await _mediator.Received(1).Send(Arg.Any<SaveClubMembersCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SkipsMember_WhenReadOrSyncReturnsFailure()
    {
        var joinedAt = DateTimeOffset.UtcNow.AddMonths(-6);
        var apiResponse = new List<ClubMemberDto>
        {
            BuildApiMember("user-1", "Player1", xp: 1000, joinedAt)
        };
        _client.ReadClubMembersAsync(ClubId, Arg.Any<CancellationToken>()).Returns(apiResponse);

        _mediator
            .Send(Arg.Is<ReadOrSyncClubMemberByUserIdQuery>(q => q.UserId == "user-1"), Arg.Any<CancellationToken>())
            .Returns(Result<ClubMember>.Failure(Error.NotFound("club_member.not_found", "missing")));

        var handler = CreateHandler();

        var statuses = await handler.Handle(new CheckGeoGuessrPlayerActivityCommand(ClubId), CancellationToken.None);

        statuses.Should().BeEmpty();
        _strikes.DidNotReceive().CreateStrike(Arg.Any<ClubMemberStrike>());
    }

    private CheckGeoGuessrPlayerActivityHandler CreateHandler()
    {
        var geoGuessrConfig = new GeoGuessrConfigurationBuilder()
            .WithClub(ClubId)
            .BuildOptions();

        var activityCheckerConfig = new ActivityCheckerConfigurationBuilder()
            .WithMinXp(100)
            .WithGracePeriodDays(7)
            .WithMaxNumStrikes(3)
            .BuildOptions();

        return new CheckGeoGuessrPlayerActivityHandler(
            _clientFactory, _strikes, _excuses, _clubs, _history,
            _messageSender, _mediator, geoGuessrConfig, activityCheckerConfig, _logger);
    }

    private static ClubMemberDto BuildApiMember(string userId, string nickname, int xp, DateTimeOffset joinedAt) =>
        new()
        {
            User = new ClubMemberUserDto
            {
                UserId = userId,
                Nick = nickname,
                Avatar = "",
                FullBodyAvatar = "",
                BorderUrl = "",
                IsVerified = false,
                Flair = 0,
                CountryCode = "us",
                TierId = 0,
                ClubUserType = 0
            },
            Role = 0,
            JoinedAt = joinedAt,
            Xp = xp,
            WeeklyXp = 0
        };
}