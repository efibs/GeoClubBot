using Entities;
using FluentAssertions;
using NSubstitute;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.DailyMissionStatistics;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.DailyMissionStatisticsTests;

public sealed class DailyMissionStatisticsHandlersTests
{
    private static readonly Guid ClubId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly IDailyMissionRepository _missions = Substitute.For<IDailyMissionRepository>();
    private readonly IDailyMissionCompletionRepository _completions = Substitute.For<IDailyMissionCompletionRepository>();
    private readonly IClubRepository _clubs = Substitute.For<IClubRepository>();

    private readonly DateOnly _today = DateOnly.FromDateTime(DateTime.UtcNow);

    public DailyMissionStatisticsHandlersTests()
    {
        _missions.ReadMissionsFetchedBetweenAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _completions.ReadCompletionsAsync(
                Arg.Any<Guid?>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([]);
    }

    private DailyMissionStatisticsHandlers CreateHandler() => new(_missions, _completions, _clubs);

    private static DailyMission BuildMission(
        DateOnly day,
        string type,
        string gameMode,
        int target,
        Guid? missionId = null,
        int fetchHourUtc = 0) =>
        DailyMission.Create(
            missionId ?? Guid.NewGuid(),
            type,
            gameMode,
            currentProgress: 0,
            targetProgress: target,
            completed: false,
            endDate: new DateTimeOffset(day.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            rewardAmount: 100,
            rewardType: "Xp",
            fetchedAtUtc: new DateTimeOffset(day.ToDateTime(new TimeOnly(fetchHourUtc, 5)), TimeSpan.Zero));

    private void ArrangeMissions(params DailyMission[] missions) =>
        _missions.ReadMissionsFetchedBetweenAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([.. missions]);

    private void ArrangeCompletions(params DailyMissionMemberCompletion[] rows) =>
        _completions.ReadCompletionsAsync(
                Arg.Any<Guid?>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([.. rows]);

    [Fact]
    public async Task Handle_DedupesMissions_StoredBySeveralFetchBatches()
    {
        var missionId = Guid.NewGuid();
        ArrangeMissions(
            BuildMission(_today.AddDays(-1), "WinGames", "Duels", target: 3, missionId, fetchHourUtc: 0),
            BuildMission(_today.AddDays(-1), "WinGames", "Duels", target: 3, missionId, fetchHourUtc: 12));

        var result = await CreateHandler().Handle(new GetDailyMissionStatisticsQuery(null, 30), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalMissionAppearances.Should().Be(1);
        result.Value.Kinds.Should().ContainSingle()
            .Which.AppearanceCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_ComputesAppearanceAndTargetStatisticsPerKind()
    {
        ArrangeMissions(
            BuildMission(_today.AddDays(-2), "WinGames", "Duels", target: 3),
            BuildMission(_today.AddDays(-1), "WinGames", "Duels", target: 5),
            BuildMission(_today, "Score", "Classic", target: 15000));

        var result = await CreateHandler().Handle(new GetDailyMissionStatisticsQuery(null, 30), CancellationToken.None);

        var stats = result.Value;
        stats.DaysWithMissionData.Should().Be(3);
        stats.TotalMissionAppearances.Should().Be(3);
        stats.Kinds.Should().HaveCount(2);

        var winDuels = stats.Kinds.Single(k => k is { Type: "WinGames", GameMode: "Duels" });
        winDuels.AppearanceCount.Should().Be(2);
        winDuels.AppearanceDayShare.Should().BeApproximately(2.0 / 3.0, 1e-9);
        winDuels.AverageTargetProgress.Should().BeApproximately(4.0, 1e-9);
        winDuels.MinTargetProgress.Should().Be(3);
        winDuels.MaxTargetProgress.Should().Be(5);
        winDuels.LastAppearance.Should().Be(_today.AddDays(-1));

        var scoreClassic = stats.Kinds.Single(k => k is { Type: "Score", GameMode: "Classic" });
        scoreClassic.AppearanceCount.Should().Be(1);
        scoreClassic.AppearanceDayShare.Should().BeApproximately(1.0 / 3.0, 1e-9);
        scoreClassic.LastAppearance.Should().Be(_today);
    }

    [Fact]
    public async Task Handle_ComputesCompletionRates_FromSnapshotRows()
    {
        var dayA = _today.AddDays(-2);
        var dayB = _today.AddDays(-1);
        ArrangeMissions(
            BuildMission(dayA, "WinGames", "Duels", target: 3),
            BuildMission(dayB, "Score", "Classic", target: 15000));
        ArrangeCompletions(
            // Day A: one of two members completed the single mission -> 50 %.
            DailyMissionMemberCompletion.Create(ClubId, "user-1", dayA, completedCount: 1),
            DailyMissionMemberCompletion.Create(ClubId, "user-2", dayA, completedCount: 0),
            // Day B: both members completed it -> 100 %.
            DailyMissionMemberCompletion.Create(ClubId, "user-1", dayB, completedCount: 1),
            DailyMissionMemberCompletion.Create(ClubId, "user-2", dayB, completedCount: 1));

        var result = await CreateHandler().Handle(new GetDailyMissionStatisticsQuery(null, 30), CancellationToken.None);

        var stats = result.Value;
        stats.AverageDayCompletionRate.Should().BeApproximately(0.75, 1e-9);
        stats.Kinds.Single(k => k.Type == "WinGames").AverageDayCompletionRateWhenPresent
            .Should().BeApproximately(0.5, 1e-9);
        stats.Kinds.Single(k => k.Type == "Score").AverageDayCompletionRateWhenPresent
            .Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public async Task Handle_BlendsAndCapsCompletionRate_WhenSeveralMissionsRunTheSameDay()
    {
        var day = _today.AddDays(-1);
        ArrangeMissions(
            BuildMission(day, "WinGames", "Duels", target: 3),
            BuildMission(day, "Score", "Classic", target: 15000));
        ArrangeCompletions(
            // 3 events / (1 member × 2 missions) would be 150 %; the rate must cap at 100 %.
            DailyMissionMemberCompletion.Create(ClubId, "user-1", day, completedCount: 3));

        var result = await CreateHandler().Handle(new GetDailyMissionStatisticsQuery(null, 30), CancellationToken.None);

        result.Value.AverageDayCompletionRate.Should().Be(1.0);
        result.Value.Kinds.Should().OnlyContain(k => k.AverageDayCompletionRateWhenPresent == 1.0);
    }

    [Fact]
    public async Task Handle_LeavesCompletionRatesNull_WithoutSnapshotData()
    {
        ArrangeMissions(BuildMission(_today.AddDays(-1), "WinGames", "Duels", target: 3));

        var result = await CreateHandler().Handle(new GetDailyMissionStatisticsQuery(null, 30), CancellationToken.None);

        result.Value.AverageDayCompletionRate.Should().BeNull();
        result.Value.Kinds.Should().ContainSingle()
            .Which.AverageDayCompletionRateWhenPresent.Should().BeNull();
    }

    [Fact]
    public async Task Handle_IgnoresSnapshotRows_OnDaysWithoutLoggedMissions()
    {
        ArrangeMissions(BuildMission(_today.AddDays(-1), "WinGames", "Duels", target: 3));
        ArrangeCompletions(
            DailyMissionMemberCompletion.Create(ClubId, "user-1", _today.AddDays(-1), completedCount: 1),
            // No missions were logged five days ago, so this day has no computable rate.
            DailyMissionMemberCompletion.Create(ClubId, "user-1", _today.AddDays(-5), completedCount: 1));

        var result = await CreateHandler().Handle(new GetDailyMissionStatisticsQuery(null, 30), CancellationToken.None);

        result.Value.AverageDayCompletionRate.Should().Be(1.0);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_ForAnUnknownClub()
    {
        _clubs.ReadClubByIdAsync(ClubId, Arg.Any<CancellationToken>()).Returns((Entities.Club?)null);

        var result = await CreateHandler().Handle(new GetDailyMissionStatisticsQuery(ClubId, 30), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_ResolvesTheClubName_AndScopesCompletionReads()
    {
        _clubs.ReadClubByIdAsync(ClubId, Arg.Any<CancellationToken>()).Returns(Entities.Club.Create(ClubId, "My Club", level: 5));

        var result = await CreateHandler().Handle(new GetDailyMissionStatisticsQuery(ClubId, 30), CancellationToken.None);

        result.Value.ClubName.Should().Be("My Club");
        await _completions.Received(1).ReadCompletionsAsync(
            ClubId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(9999, 365)]
    public async Task Handle_ClampsTheRequestedRange(int requestedDays, int effectiveDays)
    {
        var result = await CreateHandler().Handle(
            new GetDailyMissionStatisticsQuery(null, requestedDays), CancellationToken.None);

        var stats = result.Value;
        (stats.ToDay.DayNumber - stats.FromDay.DayNumber + 1).Should().Be(effectiveDays);
    }

    [Fact]
    public async Task Handle_KindsQuery_DelegatesToTheRepository()
    {
        var kinds = new List<DailyMissionKind> { new("WinGames", "Duels") };
        _missions.ReadDistinctMissionKindsAsync(Arg.Any<CancellationToken>())
            .Returns(kinds);

        var result = await CreateHandler().Handle(new GetDailyMissionKindsQuery(), CancellationToken.None);

        result.Should().BeSameAs(kinds);
    }
}
