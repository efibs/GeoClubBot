using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using Microsoft.Extensions.Logging;
using NSubstitute;
using UseCases.OutputPorts.Projections;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.ClubMemberActivity.ActivityCheckPhases;
using UseCases.UseCases.ClubMemberActivity.Rules;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.ClubMemberActivity;

public sealed class ActivityStatusCalculatorTests
{
    private static readonly Guid ClubId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly TimeRange Week = new(Now.AddDays(-7), Now);

    /// <summary>The agreed club rules: 6 streak days, 2 board missions, bonus excluded, 3 missions counted.</summary>
    private static readonly ActivityRules ClubRules = new(
        MinRuleXp: 0,
        new Dictionary<ClubXpActivityKind, int>
        {
            [ClubXpActivityKind.DailyChallengeOrDuel] = 6,
            [ClubXpActivityKind.BoardMission] = 2
        },
        new HashSet<ClubXpActivityKind> { ClubXpActivityKind.BoardClearBonus },
        new Dictionary<ClubXpActivityKind, int> { [ClubXpActivityKind.BoardMission] = 3 });

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

    private ClubMember ArrangeMember(string userId = "user-1", int xp = 1000, DateTimeOffset? joinedAt = null)
    {
        var joined = joinedAt ?? Now.AddMonths(-6);
        var persisted = new ClubMemberBuilder()
            .WithUserId(userId).WithNickname("Player1").InClub(ClubId).WithXp(xp).JoinedAt(joined).Build();
        _clubMembers.ReadClubMembersByUserIdsAsync(
                Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, ClubMember> { [userId] = persisted });

        return new ClubMemberBuilder()
            .WithUserId(userId).WithNickname("Player1").InClub(ClubId).WithXp(xp).JoinedAt(joined).Build();
    }

    private static List<ClubActivityEntry> Entries(int streakDays, int boardMissions, int bonuses = 0) =>
        Enumerable.Range(0, streakDays).Select(i => new ClubActivityEntry(ClubXpActivityKind.DailyChallengeOrDuel, 20, Now.AddDays(-6 + i)))
            .Concat(Enumerable.Range(0, boardMissions).Select(i => new ClubActivityEntry(ClubXpActivityKind.BoardMission, 20, Now.AddDays(-6).AddHours(i))))
            .Concat(Enumerable.Range(0, bonuses).Select(i => new ClubActivityEntry(ClubXpActivityKind.BoardClearBonus, 100, Now.AddDays(-5).AddHours(i))))
            .ToList();

    private Task<List<ClubMemberActivityStatus>> ExecuteAsync(
        ClubMember apiMember,
        List<ClubActivityEntry> entries,
        Dictionary<string, ClubMemberHistoryEntry>? newHistory = null,
        IEnumerable<ExcuseProjection>? excuses = null,
        ActivityRules? rules = null,
        int maxNumStrikes = 3) =>
        CreateCalculator().ExecuteAsync(
            [apiMember],
            [new LatestHistoryEntryProjection(apiMember.UserId, Xp: 1000, Timestamp: Week.From)],
            newHistory ?? [],
            excuses ?? [],
            new Dictionary<string, List<ClubActivityEntry>> { [apiMember.UserId] = entries },
            Week,
            rules ?? ClubRules,
            gracePeriod: TimeSpan.FromDays(2),
            maxNumStrikes: maxNumStrikes,
            CancellationToken.None);

    [Fact]
    public async Task Execute_DoesNotCreateStrike_WhenEveryRequirementIsMet()
    {
        var statuses = await ExecuteAsync(ArrangeMember(xp: 1160), Entries(streakDays: 6, boardMissions: 2));

        statuses.Should().ContainSingle();
        statuses[0].TargetAchieved.Should().BeTrue();
        statuses[0].XpSinceLastUpdate.Should().Be(160);
        statuses[0].RuleXp.Should().Be(160);
        _strikes.DidNotReceive().CreateStrike(Arg.Any<ClubMemberStrike>());
    }

    [Theory]
    [InlineData(5, 2)]
    [InlineData(6, 1)]
    [InlineData(0, 0)]
    public async Task Execute_CreatesStrike_WhenAnyRequirementIsMissed(int streakDays, int boardMissions)
    {
        var statuses = await ExecuteAsync(ArrangeMember(), Entries(streakDays, boardMissions));

        statuses[0].TargetAchieved.Should().BeFalse();
        statuses[0].NumStrikes.Should().Be(1);
        _strikes.Received(1).CreateStrike(Arg.Is<ClubMemberStrike>(s => s!.UserId == "user-1"));
    }

    [Fact]
    public async Task Execute_ReportsWhichRequirementWasMissed()
    {
        var statuses = await ExecuteAsync(ArrangeMember(), Entries(streakDays: 4, boardMissions: 2));

        statuses[0].FailedRequirementsText.Should().Be("streak 4/6");
    }

    [Fact]
    public async Task Execute_DoesNotCountHelpingOrTheBonusTowardsMissions()
    {
        // Lots of XP from board bonuses, but only one mission of their own: still a strike.
        var statuses = await ExecuteAsync(ArrangeMember(xp: 1500), Entries(streakDays: 7, boardMissions: 1, bonuses: 3));

        statuses[0].TargetAchieved.Should().BeFalse();
        statuses[0].XpSinceLastUpdate.Should().Be(500);
        statuses[0].RuleXp.Should().Be(160, "the board bonus is not rule XP");
    }

    [Fact]
    public async Task Execute_ScalesTheTargets_ForAMemberWhoJoinedDuringTheWeek()
    {
        // Joined 3.5 days into the 7-day window → half the week → targets floor(3), floor(1).
        var member = ArrangeMember(joinedAt: Week.From.AddDays(3.5));

        var statuses = await ExecuteAsync(member, Entries(streakDays: 3, boardMissions: 1));

        statuses[0].TargetAchieved.Should().BeTrue();
        statuses[0].RequirementResults.Select(r => r.Target).Should().Equal(3, 1);
        statuses[0].IndividualTargetReason.Should().Be("New member");
    }

    [Fact]
    public async Task Execute_OwesNothing_InsideTheGracePeriod()
    {
        var member = ArrangeMember(joinedAt: Now.AddDays(-1));

        var statuses = await ExecuteAsync(member, []);

        statuses[0].TargetAchieved.Should().BeTrue();
        statuses[0].RequirementResults.Should().OnlyContain(r => r.Target == 0);
    }

    [Fact]
    public async Task Execute_ScalesTheTargets_ForAnExcusedMember()
    {
        var member = ArrangeMember();
        var excuse = new ExcuseProjection("user-1", Week.From, Week.From.AddDays(3.5));

        var statuses = await ExecuteAsync(member, Entries(streakDays: 3, boardMissions: 1), excuses: [excuse]);

        statuses[0].TargetAchieved.Should().BeTrue();
        statuses[0].IndividualTargetReason.Should().Be("Excused");
    }

    [Fact]
    public async Task Execute_AlsoRequiresTheMinimumRuleXp_WhenConfigured()
    {
        var rules = ClubRules with { MinRuleXp = 200 };

        var statuses = await ExecuteAsync(ArrangeMember(), Entries(streakDays: 6, boardMissions: 2), rules: rules);

        statuses[0].TargetAchieved.Should().BeFalse();
        statuses[0].FailedRequirementsText.Should().Be("XP 160/200");
        statuses[0].IndividualTarget.Should().Be(200);
    }

    [Fact]
    public async Task Execute_RecordsTheIntervalOnTheNewHistorySnapshot()
    {
        var member = ArrangeMember(xp: 1300);
        var snapshot = ClubMemberHistoryEntry.Create("user-1", ClubId, 1300, Now);

        await ExecuteAsync(member, Entries(streakDays: 6, boardMissions: 5, bonuses: 1),
            newHistory: new Dictionary<string, ClubMemberHistoryEntry> { ["user-1"] = snapshot });

        snapshot.StreakDays.Should().Be(6);
        snapshot.BoardMissionCount.Should().Be(5);
        snapshot.RuleXp.Should().Be(120 + 60, "streak XP plus at most 3 missions, no bonus");
    }

    [Fact]
    public async Task Execute_FlipsIsOutOfStrikes_WhenAccruedStrikesExceedMax()
    {
        var member = ArrangeMember();
        _strikes.ReadActiveStrikeCountsByMemberUserIdsAsync(
                Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int> { ["user-1"] = 3 });

        var statuses = await ExecuteAsync(member, []);

        statuses[0].NumStrikes.Should().Be(4);
        statuses[0].IsOutOfStrikes.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_SkipsMember_WhenPersistedMemberLookupMisses()
    {
        var apiMember = new ClubMemberBuilder()
            .WithUserId("user-1").WithNickname("Player1").InClub(ClubId)
            .WithXp(1000).JoinedAt(Now.AddMonths(-6)).Build();

        // _clubMembers default is empty dict — simulates the persisted member missing.
        var statuses = await ExecuteAsync(apiMember, []);

        statuses.Should().BeEmpty();
        _strikes.DidNotReceive().CreateStrike(Arg.Any<ClubMemberStrike>());
    }
}
