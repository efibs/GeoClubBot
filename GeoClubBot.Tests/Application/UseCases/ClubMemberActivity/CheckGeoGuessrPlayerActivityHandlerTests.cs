using Configuration;
using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Notifications;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.ClubMemberActivity;
using UseCases.UseCases.ClubMemberActivity.ActivityCheckPhases;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.ClubMemberActivity;

/// <summary>
/// Covers how <see cref="CheckGeoGuessrPlayerActivityHandler"/> sequences its phases. The phases
/// themselves are the real (sealed) types wired to substituted collaborators, so the ordering the
/// handler relies on is exercised for real.
/// </summary>
public sealed class CheckGeoGuessrPlayerActivityHandlerTests
{
    private static readonly Guid ClubId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string UserId = "user-1";
    private const string Nickname = "Player1";

    private readonly IGeoGuessrClientFactory _clientFactory = Substitute.For<IGeoGuessrClientFactory>();
    private readonly IGeoGuessrClient _client = Substitute.For<IGeoGuessrClient>();
    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IStrikesRepository _strikes = Substitute.For<IStrikesRepository>();
    private readonly IClubMemberRepository _clubMembers = Substitute.For<IClubMemberRepository>();
    private readonly IExcusesRepository _excuses = Substitute.For<IExcusesRepository>();
    private readonly IHistoryRepository _history = Substitute.For<IHistoryRepository>();
    private readonly IClubRepository _clubs = Substitute.For<IClubRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IActivityStatusMessageSender _messageSender = Substitute.For<IActivityStatusMessageSender>();
    private readonly IActivityReportPublishGate _publishGate = Substitute.For<IActivityReportPublishGate>();
    private readonly IGeoGuessrActivityReader _activityReader = Substitute.For<IGeoGuessrActivityReader>();

    private readonly GeoGuessrClubEntry _clubEntry = new() { ClubId = ClubId, NcfaToken = "x", IsMain = true };
    private readonly ActivityCheckerConfiguration _activityConfig = new()
    {
        Schedule = "0 0 0 * * ?",
        TextChannelId = 1,
        MinXP = 0,
        Requirements = new Dictionary<string, int> { ["DailyChallengeOrDuel"] = 6, ["BoardMission"] = 2 },
        RuleXp = new RuleXpConfiguration
        {
            ExcludedKinds = ["BoardClearBonus"],
            MaxCountedPerWeek = new Dictionary<string, int> { ["BoardMission"] = 3 }
        },
        MaxFeedLookback = TimeSpan.FromDays(8),
        GracePeriodDays = 0,
        MaxNumStrikes = 3,
        HistoryKeepTimeSpan = TimeSpan.FromDays(60),
        StrikeDecayTimeSpan = TimeSpan.FromDays(60),
        AverageXpTopN = 1,
        AverageXpHistoryDepth = 2,
    };

    public CheckGeoGuessrPlayerActivityHandlerTests()
    {
        var joinedAt = DateTimeOffset.UtcNow.AddMonths(-6);
        var user = GeoGuessrUser.Create(UserId, Nickname);

        _clientFactory.CreateClient(Arg.Any<Guid>()).Returns(_client);
        _client.ReadClubMembersAsync(ClubId, Arg.Any<CancellationToken>())
            .Returns([BuildApiMember(UserId, Nickname, xp: 320, joinedAt)]);

        _clubs.ReadForUpdateByIdAsync(ClubId, Arg.Any<CancellationToken>())
            .Returns(Entities.Club.Create(ClubId, "club", level: 1, latestActivityCheckTime: DateTimeOffset.UtcNow.AddDays(-10)));
        _history.ReadLatestHistoryEntryProjectionsByClubIdAsync(ClubId, Arg.Any<CancellationToken>())
            .Returns([]);
        _excuses.ReadExcuseProjectionsAsync(Arg.Any<CancellationToken>()).Returns([]);
        _clubMembers.ReadClubMembersByUserIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, ClubMember>
            {
                [UserId] = ClubMember.Create(user, ClubId, xp: 320, joinedAt)
            });
        _strikes.ReadActiveStrikeCountsByMemberUserIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int>());
        _publishGate.AcquireAsync(Arg.Any<CancellationToken>()).Returns(Substitute.For<IAsyncDisposable>());
        _mediator.Send(Arg.Any<CalculateAverageXpQuery>(), Arg.Any<CancellationToken>())
            .Returns([new ClubMemberAverageXp(Nickname, AverageXp: 130, joinedAt)]);
        _activityReader.ReadActivitiesSinceAsync(ClubId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([]);
    }

    [Fact]
    public async Task Handle_ReadsTheFeed_NoFurtherBackThanTheLookback()
    {
        // The last check was 10 days ago (e.g. downtime); the check reads at most 8 days of feed.
        await CreateHandler().Handle(new CheckGeoGuessrPlayerActivityCommand(ClubId), CancellationToken.None);

        await _activityReader.Received(1).ReadActivitiesSinceAsync(
            ClubId,
            Arg.Is<DateTimeOffset>(since => since > DateTimeOffset.UtcNow.AddDays(-8).AddMinutes(-1)
                                            && since < DateTimeOffset.UtcNow.AddDays(-8).AddMinutes(1)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_JudgesTheRequirements_OnTheWindowsFeedEntriesOnly()
    {
        var now = DateTimeOffset.UtcNow;
        _clubs.ReadForUpdateByIdAsync(ClubId, Arg.Any<CancellationToken>())
            .Returns(Entities.Club.Create(ClubId, "club", level: 1, latestActivityCheckTime: now.AddDays(-7)));
        _activityReader.ReadActivitiesSinceAsync(ClubId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Enumerable.Range(0, 6).Select(i => ClubActivities.Challenge(UserId, now.AddDays(-6 + i)))
                .Append(ClubActivities.BoardMission(UserId, now.AddDays(-3)))
                // Before the window: last week's mission must not count for this week.
                .Append(ClubActivities.BoardMission(UserId, now.AddDays(-7).AddHours(-1)))
                .ToList());

        var statuses = await CreateHandler().Handle(new CheckGeoGuessrPlayerActivityCommand(ClubId), CancellationToken.None);

        statuses.Should().ContainSingle();
        statuses[0].TargetAchieved.Should().BeFalse();
        statuses[0].FailedRequirementsText.Should().Be("missions 1/2");
        await _messageSender.Received(1).SendActivityStatusUpdateMessageAsync(
            Arg.Any<List<ClubMemberActivityStatus>>(), "club", "streak 6 · missions 2", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RecordsTheWeeksRuleXpOnTheNewSnapshot()
    {
        var now = DateTimeOffset.UtcNow;
        _activityReader.ReadActivitiesSinceAsync(ClubId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([ClubActivities.Challenge(UserId, now.AddDays(-1)), ClubActivities.BoardBonus(UserId, now.AddDays(-1))]);

        await CreateHandler().Handle(new CheckGeoGuessrPlayerActivityCommand(ClubId), CancellationToken.None);

        _history.Received(1).CreateHistoryEntries(
            Arg.Is<ICollection<ClubMemberHistoryEntry>>(entries => entries!.Single().RuleXp == 20));
    }

    [Fact]
    public async Task Handle_CommitsTheNewHistorySnapshot_BeforeTheAverageXpRollupReadsIt()
    {
        // Regression for #236: the rollup dispatches CalculateAverageXpQuery, which re-reads the
        // history table from the database. If the snapshot recorded by THIS check is still sitting
        // in the change tracker, the query only sees the older rows and averages the N intervals
        // BEFORE the one that just closed (reporting e.g. 100 instead of 130).
        await CreateHandler().Handle(new CheckGeoGuessrPlayerActivityCommand(ClubId), CancellationToken.None);

        Received.InOrder(() =>
        {
            _history.CreateHistoryEntries(Arg.Any<ICollection<ClubMemberHistoryEntry>>());
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
            _mediator.Send(Arg.Any<CalculateAverageXpQuery>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Handle_RecordsASnapshotOfTheCurrentRosterXp_AndForwardsTheRollupResult()
    {
        await CreateHandler().Handle(new CheckGeoGuessrPlayerActivityCommand(ClubId), CancellationToken.None);

        // The snapshot closing the interval carries the roster XP, so the newest difference the
        // rollup averages is "XP gained since the previous check".
        _history.Received(1).CreateHistoryEntries(
            Arg.Is<ICollection<ClubMemberHistoryEntry>>(entries =>
                entries!.Count == 1 && entries.Single().UserId == UserId && entries.Single().Xp == 320));

        await _mediator.Received(1).Send(
            Arg.Is<CalculateAverageXpQuery>(q => q!.ClubId == ClubId && q.HistoryDepth == 2),
            Arg.Any<CancellationToken>());
        await _messageSender.Received(1).SendAverageXpMessageAsync(
            Arg.Is<List<ClubMemberAverageXp>>(top => top!.Count == 1 && top[0].AverageXp == 130),
            Arg.Any<List<ClubMemberAverageXp>>(),
            Arg.Any<string>(),
            2,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_StillCommits_WhenTheAverageXpRollupIsDisabled()
    {
        _activityConfig.AverageXpTopN = null;
        _activityConfig.AverageXpBottomN = null;

        await CreateHandler().Handle(new CheckGeoGuessrPlayerActivityCommand(ClubId), CancellationToken.None);

        await _mediator.DidNotReceive().Send(Arg.Any<CalculateAverageXpQuery>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private CheckGeoGuessrPlayerActivityHandler CreateHandler()
    {
        var geoGuessrConfig = new GeoGuessrConfiguration
        {
            SyncSchedule = "0 0 0 * * ?",
            ActivityNcfaToken = "x",
            UserProfileNcfaToken = "x",
            Clubs = [_clubEntry],
        };

        return new CheckGeoGuessrPlayerActivityHandler(
            new ActivityCheckSyncStep(_clientFactory, _mediator),
            new ActivityStatusCalculator(_strikes, _clubMembers, NullLogger<ActivityStatusCalculator>.Instance),
            new ActivityAverageXpRollupStep(_mediator, _messageSender),
            _excuses,
            _history,
            _clubs,
            _unitOfWork,
            _messageSender,
            _publishGate,
            _activityReader,
            ClubActivities.Classifier(),
            Options.Create(geoGuessrConfig),
            Options.Create(_activityConfig),
            NullLogger<CheckGeoGuessrPlayerActivityHandler>.Instance);
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
                ClubUserType = 0,
            },
            Role = 0,
            JoinedAt = joinedAt,
            Xp = xp,
            WeeklyXp = 0,
        };
}
