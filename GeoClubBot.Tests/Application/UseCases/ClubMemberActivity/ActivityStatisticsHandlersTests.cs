using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using NSubstitute;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.ClubMemberActivity;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.ClubMemberActivity;

public sealed class ActivityStatisticsHandlersTests
{
    private const string Nickname = "Player1";

    private static readonly Guid ClubId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly IClubRepository _clubs = Substitute.For<IClubRepository>();
    private readonly IClubMemberRepository _clubMembers = Substitute.For<IClubMemberRepository>();
    private readonly IHistoryRepository _history = Substitute.For<IHistoryRepository>();

    private ActivityStatisticsHandlers CreateHandler() =>
        new(_clubs, _clubMembers, _history, new GeoGuessrConfigurationBuilder().WithClub(ClubId).BuildOptions());

    private static ClubMemberHistoryEntry Entry(string userId, int week, int xp) =>
        ClubMemberHistoryEntry.Create(userId, ClubId, xp, Start.AddDays(7 * week));

    private void ClubExists() =>
        _clubs.ReadClubByIdAsync(ClubId, Arg.Any<CancellationToken>())
            .Returns(global::Entities.Club.Create(ClubId, "Main", 1));

    private void MemberExists(Guid? clubId) =>
        _clubMembers.ReadClubMemberByNicknameAsync(Nickname, Arg.Any<CancellationToken>())
            .Returns(new ClubMemberBuilder().WithNickname(Nickname).InClub(clubId).Build());

    private void PlayerHistoryIs(params ClubMemberHistoryEntry[] entries) =>
        _history.ReadHistoryEntriesByPlayerNicknameAsync(Nickname, ClubId, Arg.Any<CancellationToken>())
            .Returns([.. entries]);

    // ---- Club -------------------------------------------------------------

    [Fact]
    public async Task Club_UnknownMainClub_HasNoStatistics()
    {
        _clubs.ReadClubByIdAsync(ClubId, Arg.Any<CancellationToken>()).Returns((global::Entities.Club?)null);

        var stats = await CreateHandler().Handle(new ClubStatisticsQuery(), CancellationToken.None);

        stats.Should().BeNull();
    }

    [Fact]
    public async Task Club_WithoutHistory_HasNoStatistics()
    {
        ClubExists();
        _history.ReadHistoryEntriesAsync(ClubId, Arg.Any<CancellationToken>()).Returns([]);

        var stats = await CreateHandler().Handle(new ClubStatisticsQuery(), CancellationToken.None);

        stats.Should().BeNull();
    }

    [Fact]
    public async Task Club_AveragesEachMembersGains_AndSummarisesThoseAverages()
    {
        ClubExists();
        // Unordered on purpose, like the repository: gains only telescope in chronological order.
        // Averages per member, sorted: 10, 20, 30, 40, 50.
        _history.ReadHistoryEntriesAsync(ClubId, Arg.Any<CancellationToken>()).Returns(
        [
            Entry("a", 1, 20), Entry("a", 0, 10),
            Entry("b", 0, 20), Entry("b", 1, 40),
            Entry("c", 1, 60), Entry("c", 0, 30),
            Entry("d", 0, 40), Entry("d", 1, 80),
            Entry("e", 1, 100), Entry("e", 0, 50)
        ]);

        var stats = await CreateHandler().Handle(new ClubStatisticsQuery(), CancellationToken.None);

        stats.Should().Be(new ClubStatistics("Main", 30, 10, 20, 30, 40, 50));
    }

    [Fact]
    public async Task Club_TakesTheSnapshotsInChronologicalOrder()
    {
        ClubExists();
        _history.ReadHistoryEntriesAsync(ClubId, Arg.Any<CancellationToken>()).Returns(
        [
            Entry("a", 2, 90),
            Entry("a", 0, 30),
            Entry("a", 1, 60)
        ]);

        var stats = await CreateHandler().Handle(new ClubStatisticsQuery(), CancellationToken.None);

        stats!.AverageAveragePoints.Should().Be(30);
    }

    [Fact]
    public async Task Club_ReadsTheHistoryOfTheMainClub()
    {
        ClubExists();
        _history.ReadHistoryEntriesAsync(ClubId, Arg.Any<CancellationToken>()).Returns([Entry("a", 0, 10)]);

        var stats = await CreateHandler().Handle(new ClubStatisticsQuery(), CancellationToken.None);

        stats!.ClubName.Should().Be("Main");
    }

    // ---- Player -----------------------------------------------------------

    [Fact]
    public async Task Player_Unknown_HasNoStatistics()
    {
        _clubMembers.ReadClubMemberByNicknameAsync(Nickname, Arg.Any<CancellationToken>()).Returns((ClubMember?)null);

        var stats = await CreateHandler().Handle(new PlayerStatisticsQuery(Nickname), CancellationToken.None);

        stats.Should().BeNull();
    }

    [Fact]
    public async Task Player_WhoLeftTheClub_HasNoStatistics()
    {
        MemberExists(clubId: null);

        var stats = await CreateHandler().Handle(new PlayerStatisticsQuery(Nickname), CancellationToken.None);

        stats.Should().BeNull();
    }

    [Fact]
    public async Task Player_WithUnreadableHistory_HasNoStatistics()
    {
        MemberExists(ClubId);
        _history.ReadHistoryEntriesByPlayerNicknameAsync(Nickname, ClubId, Arg.Any<CancellationToken>())
            .Returns((List<ClubMemberHistoryEntry>?)null);

        var stats = await CreateHandler().Handle(new PlayerStatisticsQuery(Nickname), CancellationToken.None);

        stats.Should().BeNull();
    }

    [Fact]
    public async Task Player_WithoutHistory_IsAllZeros_ButNamed()
    {
        MemberExists(ClubId);
        PlayerHistoryIs();

        var stats = await CreateHandler().Handle(new PlayerStatisticsQuery(Nickname), CancellationToken.None);

        stats.Should().BeEquivalentTo(
            new PlayerStatistics(Nickname, default, 0, 0, 0, 0, 0, 0, 0),
            options => options.Excluding(s => s.HistorySince));
        stats!.HistorySince.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Player_WithASingleSnapshot_HasNoGainsYet()
    {
        // Every new member has exactly this after their first weekly check.
        MemberExists(ClubId);
        PlayerHistoryIs(Entry("a", 0, 500));

        var stats = await CreateHandler().Handle(new PlayerStatisticsQuery(Nickname), CancellationToken.None);

        stats.Should().Be(new PlayerStatistics(Nickname, Start, 0, 0, 0, 0, 0, 0, 0));
    }

    [Fact]
    public async Task Player_SummarisesTheGainsBetweenSnapshots()
    {
        MemberExists(ClubId);
        // Gains: 40, 10, 20, 30 — sorted only by time, as the quartiles are taken from that order.
        PlayerHistoryIs(Entry("a", 3, 170), Entry("a", 0, 100), Entry("a", 4, 200), Entry("a", 1, 140), Entry("a", 2, 150));

        var stats = await CreateHandler().Handle(new PlayerStatisticsQuery(Nickname), CancellationToken.None);

        stats.Should().Be(new PlayerStatistics(Nickname, Start, 4, 25, 10, 10, 20, 30, 40));
    }
}
