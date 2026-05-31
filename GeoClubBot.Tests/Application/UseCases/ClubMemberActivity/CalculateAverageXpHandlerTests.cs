using FluentAssertions;
using NSubstitute;
using UseCases.OutputPorts.Repositories;
using UseCases.OutputPorts.Projections;
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
        var joinedAt = DateTimeOffset.UtcNow.AddYears(-1);
        var entries = new List<HistoryEntryProjection>
        {
            new("u1", Xp: 100, Timestamp: t0,            MemberNickname: "Player1", MemberJoinedAt: joinedAt),
            new("u1", Xp: 200, Timestamp: t0.AddDays(1), MemberNickname: "Player1", MemberJoinedAt: joinedAt),
            new("u1", Xp: 300, Timestamp: t0.AddDays(2), MemberNickname: "Player1", MemberJoinedAt: joinedAt),
            new("u1", Xp: 400, Timestamp: t0.AddDays(3), MemberNickname: "Player1", MemberJoinedAt: joinedAt),
            new("u1", Xp: 500, Timestamp: t0.AddDays(4), MemberNickname: "Player1", MemberJoinedAt: joinedAt)
        };

        _history.ReadHistoryEntryProjectionsByClubIdAsync(ClubId, Arg.Any<CancellationToken>())
            .Returns(entries);
        _excuses.ReadExcuseProjectionsAsync(Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await CreateHandler().Handle(new CalculateAverageXpQuery(ClubId, 3), CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].AverageXp.Should().Be(100);
    }

    [Fact]
    public async Task Handle_SkipsUser_WhenFewerThanTwoEntries()
    {
        var entries = new List<HistoryEntryProjection>
        {
            new("u1", Xp: 100, Timestamp: DateTimeOffset.UtcNow,
                MemberNickname: "Player1", MemberJoinedAt: DateTimeOffset.UtcNow.AddYears(-1))
        };

        _history.ReadHistoryEntryProjectionsByClubIdAsync(ClubId, Arg.Any<CancellationToken>())
            .Returns(entries);
        _excuses.ReadExcuseProjectionsAsync(Arg.Any<CancellationToken>())
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
        var joinedAt = DateTimeOffset.UtcNow.AddYears(-1);
        var entries = new List<HistoryEntryProjection>
        {
            new("u1", Xp: 100, Timestamp: t0,            MemberNickname: "Player1", MemberJoinedAt: joinedAt),
            new("u1", Xp: 150, Timestamp: t0.AddDays(1), MemberNickname: "Player1", MemberJoinedAt: joinedAt),
            new("u1", Xp: 250, Timestamp: t0.AddDays(2), MemberNickname: "Player1", MemberJoinedAt: joinedAt)
        };

        // Excuse covering the gap between the two most recent entries (t0+1 → t0+2).
        var excuses = new List<ExcuseProjection>
        {
            new("u1", From: t0.AddDays(1).AddHours(-1), To: t0.AddDays(2).AddHours(1))
        };

        _history.ReadHistoryEntryProjectionsByClubIdAsync(ClubId, Arg.Any<CancellationToken>())
            .Returns(entries);
        _excuses.ReadExcuseProjectionsAsync(Arg.Any<CancellationToken>())
            .Returns(excuses);

        var result = await CreateHandler().Handle(new CalculateAverageXpQuery(ClubId, 2), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_UsesUserIdAsNicknameFallback_WhenProjectionNicknameIsNull()
    {
        // Projection's MemberNickname can be null when the parent ClubMember row has been
        // deleted (LEFT JOIN). Verify the handler falls back to UserId.
        var t0 = DateTimeOffset.UtcNow.AddDays(-2);
        var entries = new List<HistoryEntryProjection>
        {
            new("user-42", Xp: 100, Timestamp: t0,            MemberNickname: null, MemberJoinedAt: null),
            new("user-42", Xp: 200, Timestamp: t0.AddDays(1), MemberNickname: null, MemberJoinedAt: null)
        };

        _history.ReadHistoryEntryProjectionsByClubIdAsync(ClubId, Arg.Any<CancellationToken>())
            .Returns(entries);
        _excuses.ReadExcuseProjectionsAsync(Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await CreateHandler().Handle(new CalculateAverageXpQuery(ClubId, 1), CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Nickname.Should().Be("user-42");
    }
}
