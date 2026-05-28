using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using NSubstitute;
using UseCases.OutputPorts;
using UseCases.UseCases.ClubMemberActivity;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.ClubMemberActivity;

public sealed class CalculateAverageXpHandlerTests
{
    private static readonly Guid ClubId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly IHistoryRepository _history = Substitute.For<IHistoryRepository>();
    private readonly IExcusesRepository _excuses = Substitute.For<IExcusesRepository>();

    private CalculateAverageXpHandler CreateHandler() => new(_history, _excuses);

    [Fact]
    public async Task Handle_ComputesAverage_OverTheRequestedHistoryDepth()
    {
        // 5 entries → 4 deltas (oldest → newest each step +100 XP).
        // HistoryDepth=3 should take the 3 most recent deltas only.
        var t0 = DateTimeOffset.UtcNow.AddDays(-5);
        var entries = new List<ClubMemberHistoryEntry>
        {
            new HistoryEntryBuilder().WithUserId("u1").InClub(ClubId).WithXp(100).At(t0).Build(),
            new HistoryEntryBuilder().WithUserId("u1").InClub(ClubId).WithXp(200).At(t0.AddDays(1)).Build(),
            new HistoryEntryBuilder().WithUserId("u1").InClub(ClubId).WithXp(300).At(t0.AddDays(2)).Build(),
            new HistoryEntryBuilder().WithUserId("u1").InClub(ClubId).WithXp(400).At(t0.AddDays(3)).Build(),
            new HistoryEntryBuilder().WithUserId("u1").InClub(ClubId).WithXp(500).At(t0.AddDays(4)).Build()
        };

        _history.ReadHistoryEntriesByClubIdAsync(ClubId, Arg.Any<CancellationToken>())
            .Returns(entries);
        _excuses.ReadExcusesAsync(Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await CreateHandler().Handle(new CalculateAverageXpQuery(ClubId, 3), CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].AverageXp.Should().Be(100); // every delta is +100
    }

    [Fact]
    public async Task Handle_SkipsUser_WhenFewerThanTwoEntries()
    {
        var entries = new List<ClubMemberHistoryEntry>
        {
            new HistoryEntryBuilder().WithUserId("u1").InClub(ClubId).WithXp(100).At(DateTimeOffset.UtcNow).Build()
        };

        _history.ReadHistoryEntriesByClubIdAsync(ClubId, Arg.Any<CancellationToken>())
            .Returns(entries);
        _excuses.ReadExcusesAsync(Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await CreateHandler().Handle(new CalculateAverageXpQuery(ClubId, 1), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SkipsUser_WhenInsufficientNonExcusedWindowsForHistoryDepth()
    {
        // 3 entries → 2 windows. With HistoryDepth=2 and one window fully excused,
        // the user has only 1 valid delta — not enough — and is dropped from results.
        var t0 = DateTimeOffset.UtcNow.AddDays(-3);
        var entries = new List<ClubMemberHistoryEntry>
        {
            new HistoryEntryBuilder().WithUserId("u1").InClub(ClubId).WithXp(100).At(t0).Build(),
            new HistoryEntryBuilder().WithUserId("u1").InClub(ClubId).WithXp(150).At(t0.AddDays(1)).Build(),
            new HistoryEntryBuilder().WithUserId("u1").InClub(ClubId).WithXp(250).At(t0.AddDays(2)).Build()
        };

        // Excuse covering the gap between the two most recent entries (t0+1 → t0+2).
        var excuses = new List<ClubMemberExcuse>
        {
            new ExcuseBuilder().WithUserId("u1").Covering(t0.AddDays(1).AddHours(-1), t0.AddDays(2).AddHours(1)).Build()
        };

        _history.ReadHistoryEntriesByClubIdAsync(ClubId, Arg.Any<CancellationToken>())
            .Returns(entries);
        _excuses.ReadExcusesAsync(Arg.Any<CancellationToken>())
            .Returns(excuses);

        var result = await CreateHandler().Handle(new CalculateAverageXpQuery(ClubId, 2), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_UsesUserIdAsNicknameFallback_WhenNoClubMemberNavigationPropertyAvailable()
    {
        // ClubMemberHistoryEntry.Create does not populate the ClubMember navigation property
        // (EF would set it). Verify the handler falls back to UserId for the nickname.
        var t0 = DateTimeOffset.UtcNow.AddDays(-2);
        var entries = new List<ClubMemberHistoryEntry>
        {
            new HistoryEntryBuilder().WithUserId("user-42").InClub(ClubId).WithXp(100).At(t0).Build(),
            new HistoryEntryBuilder().WithUserId("user-42").InClub(ClubId).WithXp(200).At(t0.AddDays(1)).Build()
        };

        _history.ReadHistoryEntriesByClubIdAsync(ClubId, Arg.Any<CancellationToken>())
            .Returns(entries);
        _excuses.ReadExcusesAsync(Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await CreateHandler().Handle(new CalculateAverageXpQuery(ClubId, 1), CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Nickname.Should().Be("user-42");
    }
}
