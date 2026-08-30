using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using NSubstitute;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.DailyMissionStatistics;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.DailyMissionStatisticsTests;

public sealed class GetDailyMissionStreaksHandlerTests
{
    private static readonly Guid ClubId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly IDailyMissionCompletionRepository _completions = Substitute.For<IDailyMissionCompletionRepository>();
    private readonly IClubMemberRepository _members = Substitute.For<IClubMemberRepository>();

    private readonly DateOnly _today = DateOnly.FromDateTime(DateTime.UtcNow);

    public GetDailyMissionStreaksHandlerTests()
    {
        _completions.ReadCompletionsAsync(
                Arg.Any<Guid?>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _members.ReadClubMembersByClubIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([]);
    }

    private GetDailyMissionStreaksHandler CreateHandler() => new(_completions, _members);

    private void ArrangeCompletions(params DailyMissionMemberCompletion[] rows) =>
        _completions.ReadCompletionsAsync(
                Arg.Any<Guid?>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([.. rows]);

    private void ArrangeMembers(params ClubMember[] members) =>
        _members.ReadClubMembersByClubIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([.. members]);

    private static DailyMissionMemberCompletion Completed(string userId, DateOnly date, int count = 1) =>
        DailyMissionMemberCompletion.Create(ClubId, userId, date, count);

    private static ClubMember Member(string userId, string nickname) =>
        new ClubMemberBuilder().WithUserId(userId).WithNickname(nickname).InClub(ClubId).Build();

    [Fact]
    public async Task Handle_ReturnsEmpty_WhenNoCompletions()
    {
        var result = await CreateHandler().Handle(new GetDailyMissionStreaksQuery(ClubId, 30), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_CountsConsecutiveDaysEndingToday()
    {
        ArrangeMembers(Member("u1", "Alice"));
        ArrangeCompletions(
            Completed("u1", _today),
            Completed("u1", _today.AddDays(-1)),
            Completed("u1", _today.AddDays(-2)));

        var result = await CreateHandler().Handle(new GetDailyMissionStreaksQuery(ClubId, 30), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Nickname.Should().Be("Alice");
        result[0].CurrentStreak.Should().Be(3);
        result[0].LongestStreak.Should().Be(3);
    }

    [Fact]
    public async Task Handle_AllowsGrace_WhenTodayNotYetSnapshotted()
    {
        ArrangeMembers(Member("u1", "Alice"));
        ArrangeCompletions(
            Completed("u1", _today.AddDays(-1)),
            Completed("u1", _today.AddDays(-2)));

        var result = await CreateHandler().Handle(new GetDailyMissionStreaksQuery(ClubId, 30), CancellationToken.None);

        result[0].CurrentStreak.Should().Be(2);
    }

    [Fact]
    public async Task Handle_BreaksCurrentStreakOnGap_ButLongestCapturesBestRun()
    {
        ArrangeMembers(Member("u1", "Alice"));
        ArrangeCompletions(
            // Current run is just today (a gap sits at yesterday).
            Completed("u1", _today),
            // An older run of three consecutive days.
            Completed("u1", _today.AddDays(-3)),
            Completed("u1", _today.AddDays(-4)),
            Completed("u1", _today.AddDays(-5)));

        var result = await CreateHandler().Handle(new GetDailyMissionStreaksQuery(ClubId, 30), CancellationToken.None);

        result[0].CurrentStreak.Should().Be(1);
        result[0].LongestStreak.Should().Be(3);
    }

    [Fact]
    public async Task Handle_IgnoresZeroCompletionDays()
    {
        ArrangeMembers(Member("u1", "Alice"));
        ArrangeCompletions(
            Completed("u1", _today),
            Completed("u1", _today.AddDays(-1), count: 0),
            Completed("u1", _today.AddDays(-2)));

        var result = await CreateHandler().Handle(new GetDailyMissionStreaksQuery(ClubId, 30), CancellationToken.None);

        result[0].CurrentStreak.Should().Be(1);
        result[0].LongestStreak.Should().Be(1);
    }

    [Fact]
    public async Task Handle_SkipsDepartedMembers_WithoutNicknames()
    {
        ArrangeMembers(Member("u1", "Alice"));
        ArrangeCompletions(
            Completed("u1", _today),
            // u2 has completions but is no longer a club member, so it can't be named.
            Completed("u2", _today));

        var result = await CreateHandler().Handle(new GetDailyMissionStreaksQuery(ClubId, 30), CancellationToken.None);

        result.Should().ContainSingle().Which.Nickname.Should().Be("Alice");
    }

    [Fact]
    public async Task Handle_RanksByCurrentThenLongestThenNickname()
    {
        ArrangeMembers(Member("u1", "Alice"), Member("u2", "Bob"), Member("u3", "Cara"));
        ArrangeCompletions(
            // Alice: current 1, longest 1.
            Completed("u1", _today),
            // Bob: current 2, longest 2.
            Completed("u2", _today),
            Completed("u2", _today.AddDays(-1)),
            // Cara: current 2, longest 5 (older run).
            Completed("u3", _today),
            Completed("u3", _today.AddDays(-1)),
            Completed("u3", _today.AddDays(-4)),
            Completed("u3", _today.AddDays(-5)),
            Completed("u3", _today.AddDays(-6)),
            Completed("u3", _today.AddDays(-7)),
            Completed("u3", _today.AddDays(-8)));

        var result = await CreateHandler().Handle(new GetDailyMissionStreaksQuery(ClubId, 30), CancellationToken.None);

        result.Select(s => s.Nickname).Should().ContainInOrder("Cara", "Bob", "Alice");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Handle_ClampsNonPositiveWindow_ToAtLeastOneDay(int windowDays)
    {
        ArrangeMembers(Member("u1", "Alice"));
        ArrangeCompletions(Completed("u1", _today));

        var result = await CreateHandler().Handle(new GetDailyMissionStreaksQuery(ClubId, windowDays), CancellationToken.None);

        result.Should().ContainSingle().Which.CurrentStreak.Should().Be(1);
    }
}
