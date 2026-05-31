using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using Microsoft.Extensions.Logging;
using NSubstitute;
using UseCases.OutputPorts.Repositories;
using UseCases.OutputPorts.Projections;
using UseCases.UseCases.ClubMemberActivity.ActivityCheckPhases;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.ClubMemberActivity;

public sealed class ActivityStatusCalculatorTests
{
    private static readonly Guid ClubId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly IStrikesRepository _strikes = Substitute.For<IStrikesRepository>();
    private readonly IClubMemberRepository _clubMembers = Substitute.For<IClubMemberRepository>();
    private readonly ILogger<ActivityStatusCalculator> _logger = Substitute.For<ILogger<ActivityStatusCalculator>>();

    public ActivityStatusCalculatorTests()
    {
        _strikes.ReadActiveStrikeCountsByMemberUserIdsAsync(
                Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int>());
        _clubMembers.ReadClubMembersByUserIdsAsync(
                Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, ClubMember>());
    }

    private ActivityStatusCalculator CreateCalculator() => new(_strikes, _clubMembers, _logger);

    [Fact]
    public async Task Execute_CreatesStrike_WhenMemberFallsShortOfTarget()
    {
        // Member joined 6 months ago (well past the 7-day grace period). Prior history shows
        // 1000 XP; API still says 1000 XP → delta = 0, target = 100, so a strike is created.
        var joinedAt = DateTimeOffset.UtcNow.AddMonths(-6);
        var apiMember = new ClubMemberBuilder()
            .WithUserId("user-1").WithNickname("Player1").InClub(ClubId)
            .WithXp(1000).JoinedAt(joinedAt).Build();

        var persistedMember = new ClubMemberBuilder()
            .WithUserId("user-1").WithNickname("Player1").InClub(ClubId)
            .WithXp(1000).JoinedAt(joinedAt).Build();
        _clubMembers.ReadClubMembersByUserIdsAsync(
                Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, ClubMember> { ["user-1"] = persistedMember });

        var latest = new List<LatestHistoryEntryProjection>
        {
            new("user-1", Xp: 1000, Timestamp: DateTimeOffset.UtcNow.AddDays(-7))
        };

        var statuses = await CreateCalculator().ExecuteAsync(
            [apiMember], latest, [],
            lastActivityCheckTime: DateTimeOffset.UtcNow.AddDays(-7),
            now: DateTimeOffset.UtcNow,
            xpRequirement: 100,
            gracePeriod: TimeSpan.FromDays(7),
            maxNumStrikes: 3,
            CancellationToken.None);

        statuses.Should().HaveCount(1);
        statuses[0].TargetAchieved.Should().BeFalse();
        statuses[0].NumStrikes.Should().Be(1);
        statuses[0].IsOutOfStrikes.Should().BeFalse();
        _strikes.Received(1).CreateStrike(Arg.Is<ClubMemberStrike>(s => s.UserId == "user-1"));
    }

    [Fact]
    public async Task Execute_DoesNotCreateStrike_WhenMemberMeetsTarget()
    {
        // Member earned 200 XP since last entry; target = 100 → no strike.
        var joinedAt = DateTimeOffset.UtcNow.AddMonths(-6);
        var apiMember = new ClubMemberBuilder()
            .WithUserId("user-1").WithNickname("Player1").InClub(ClubId)
            .WithXp(1200).JoinedAt(joinedAt).Build();

        var persistedMember = new ClubMemberBuilder()
            .WithUserId("user-1").WithNickname("Player1").InClub(ClubId)
            .WithXp(1200).JoinedAt(joinedAt).Build();
        _clubMembers.ReadClubMembersByUserIdsAsync(
                Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, ClubMember> { ["user-1"] = persistedMember });

        var latest = new List<LatestHistoryEntryProjection>
        {
            new("user-1", Xp: 1000, Timestamp: DateTimeOffset.UtcNow.AddDays(-7))
        };

        var statuses = await CreateCalculator().ExecuteAsync(
            [apiMember], latest, [],
            lastActivityCheckTime: DateTimeOffset.UtcNow.AddDays(-7),
            now: DateTimeOffset.UtcNow,
            xpRequirement: 100,
            gracePeriod: TimeSpan.FromDays(7),
            maxNumStrikes: 3,
            CancellationToken.None);

        statuses.Should().HaveCount(1);
        statuses[0].TargetAchieved.Should().BeTrue();
        statuses[0].XpSinceLastUpdate.Should().Be(200);
        _strikes.DidNotReceive().CreateStrike(Arg.Any<ClubMemberStrike>());
    }

    [Fact]
    public async Task Execute_FlipsIsOutOfStrikes_WhenAccruedStrikesExceedMax()
    {
        // maxNumStrikes = 3; member already has 3 active strikes; this run accrues a 4th
        // (delta = 0, target = 100).
        var joinedAt = DateTimeOffset.UtcNow.AddMonths(-6);
        var apiMember = new ClubMemberBuilder()
            .WithUserId("user-1").WithNickname("Player1").InClub(ClubId)
            .WithXp(1000).JoinedAt(joinedAt).Build();

        var persistedMember = new ClubMemberBuilder()
            .WithUserId("user-1").WithNickname("Player1").InClub(ClubId)
            .WithXp(1000).JoinedAt(joinedAt).Build();
        _clubMembers.ReadClubMembersByUserIdsAsync(
                Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, ClubMember> { ["user-1"] = persistedMember });
        _strikes.ReadActiveStrikeCountsByMemberUserIdsAsync(
                Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int> { ["user-1"] = 3 });

        var latest = new List<LatestHistoryEntryProjection>
        {
            new("user-1", Xp: 1000, Timestamp: DateTimeOffset.UtcNow.AddDays(-7))
        };

        var statuses = await CreateCalculator().ExecuteAsync(
            [apiMember], latest, [],
            lastActivityCheckTime: DateTimeOffset.UtcNow.AddDays(-7),
            now: DateTimeOffset.UtcNow,
            xpRequirement: 100,
            gracePeriod: TimeSpan.FromDays(7),
            maxNumStrikes: 3,
            CancellationToken.None);

        statuses.Should().HaveCount(1);
        statuses[0].NumStrikes.Should().Be(4);
        statuses[0].IsOutOfStrikes.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_SkipsMember_WhenPersistedMemberLookupMisses()
    {
        var joinedAt = DateTimeOffset.UtcNow.AddMonths(-6);
        var apiMember = new ClubMemberBuilder()
            .WithUserId("user-1").WithNickname("Player1").InClub(ClubId)
            .WithXp(1000).JoinedAt(joinedAt).Build();

        // _clubMembers default is empty dict — simulates the persisted member missing.

        var statuses = await CreateCalculator().ExecuteAsync(
            [apiMember], [], [],
            lastActivityCheckTime: DateTimeOffset.UtcNow.AddDays(-7),
            now: DateTimeOffset.UtcNow,
            xpRequirement: 100,
            gracePeriod: TimeSpan.FromDays(7),
            maxNumStrikes: 3,
            CancellationToken.None);

        statuses.Should().BeEmpty();
        _strikes.DidNotReceive().CreateStrike(Arg.Any<ClubMemberStrike>());
    }
}
