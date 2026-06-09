using Configuration;
using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.DailyMissionStatistics;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.DailyMissionStatisticsTests;

public sealed class SnapshotDailyMissionCompletionsHandlerTests
{
    private static readonly Guid ClubA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClubB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const int DailyMissionXpReward = 20;

    private readonly IDailyMissionCompletionRepository _completions = Substitute.For<IDailyMissionCompletionRepository>();
    private readonly IClubMemberRepository _members = Substitute.For<IClubMemberRepository>();
    private readonly IGeoGuessrActivityReader _activityReader = Substitute.For<IGeoGuessrActivityReader>();
    private readonly ILogger<SnapshotDailyMissionCompletionsHandler> _logger =
        Substitute.For<ILogger<SnapshotDailyMissionCompletionsHandler>>();

    private readonly DateOnly _yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);

    public SnapshotDailyMissionCompletionsHandlerTests()
    {
        _members.ReadClubMembersByClubIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _activityReader.ReadActivitiesSinceAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([]);
    }

    private SnapshotDailyMissionCompletionsHandler CreateHandler(params Guid[] clubIds) => new(
        _completions,
        _members,
        _activityReader,
        Options.Create(new DailyMissionReminderConfiguration
        {
            Schedule = "0 * * * * ?",
            DefaultMessage = "x",
            DailyMissionXpReward = DailyMissionXpReward
        }),
        Options.Create(new GeoGuessrConfiguration
        {
            SyncSchedule = "0 0 0 * * ?",
            ActivityNcfaToken = "x",
            MissionsNcfaToken = "x",
            UserProfileNcfaToken = "x",
            Clubs = clubIds
                .Select((id, i) => new GeoGuessrClubEntry { ClubId = id, NcfaToken = "x", IsMain = i == 0 })
                .ToList(),
        }),
        _logger);

    private static ReadClubActivitiesItemDto BuildActivity(string userId, int xpReward, DateTimeOffset recordedAt) => new()
    {
        UserId = userId,
        XpReward = xpReward,
        RecordedAt = recordedAt
    };

    private DateTimeOffset YesterdayAt(int hour) =>
        new(_yesterday.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero);

    [Fact]
    public async Task Handle_WritesOneRowPerMember_CountingOnlyYesterdaysMissionRewardEvents()
    {
        var memberDone = new ClubMemberBuilder().WithUserId("user-done-000000000000000").InClub(ClubA).Build();
        var memberIdle = new ClubMemberBuilder().WithUserId("user-idle-000000000000000").InClub(ClubA).Build();
        _members.ReadClubMembersByClubIdAsync(ClubA, Arg.Any<CancellationToken>())
            .Returns([memberDone, memberIdle]);

        _activityReader.ReadActivitiesSinceAsync(ClubA, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([
                BuildActivity(memberDone.UserId, DailyMissionXpReward, YesterdayAt(8)),
                BuildActivity(memberDone.UserId, DailyMissionXpReward, YesterdayAt(20)),
                // Wrong XP amount: a regular game, not a mission completion.
                BuildActivity(memberDone.UserId, 150, YesterdayAt(9)),
                // Right XP amount but recorded today, i.e. outside the snapshotted day.
                BuildActivity(memberIdle.UserId, DailyMissionXpReward, YesterdayAt(8).AddDays(1)),
            ]);

        IEnumerable<DailyMissionMemberCompletion>? written = null;
        _completions.AddRange(Arg.Do<IEnumerable<DailyMissionMemberCompletion>>(rows => written = rows.ToList()));

        await CreateHandler(ClubA).Handle(new SnapshotDailyMissionCompletionsCommand(), CancellationToken.None);

        written.Should().NotBeNull();
        written.Should().HaveCount(2);
        written.Should().OnlyContain(r => r.ClubId == ClubA && r.Date == _yesterday);
        written.Single(r => r.UserId == memberDone.UserId).CompletedCount.Should().Be(2);
        written.Single(r => r.UserId == memberIdle.UserId).CompletedCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_SkipsClubsThatAlreadyHaveASnapshot()
    {
        _completions.HasSnapshotForDayAsync(ClubA, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(true);

        await CreateHandler(ClubA, ClubB).Handle(new SnapshotDailyMissionCompletionsCommand(), CancellationToken.None);

        await _activityReader.DidNotReceive()
            .ReadActivitiesSinceAsync(ClubA, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _activityReader.Received(1)
            .ReadActivitiesSinceAsync(ClubB, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ContinuesWithTheRemainingClubs_WhenOneClubsFeedFails()
    {
        _activityReader.ReadActivitiesSinceAsync(ClubA, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<ReadClubActivitiesItemDto>>(_ => throw new InvalidOperationException("feed down"));

        var member = new ClubMemberBuilder().WithUserId("user-b-00000000000000000").InClub(ClubB).Build();
        _members.ReadClubMembersByClubIdAsync(ClubB, Arg.Any<CancellationToken>())
            .Returns([member]);

        await CreateHandler(ClubA, ClubB).Handle(new SnapshotDailyMissionCompletionsCommand(), CancellationToken.None);

        _completions.Received(1).AddRange(
            Arg.Is<IEnumerable<DailyMissionMemberCompletion>>(rows => rows.All(r => r.ClubId == ClubB)));
    }
}
