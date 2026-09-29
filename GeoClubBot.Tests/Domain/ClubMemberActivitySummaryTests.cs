using Entities;
using FluentAssertions;
using Xunit;

namespace GeoClubBot.Tests.Domain;

public sealed class ClubMemberActivitySummaryTests
{
    private static DayActivity Day(int dayOfMonth, bool challengeDone, int boardMissions = 0) =>
        new(new DateOnly(2026, 9, dayOfMonth), challengeDone, boardMissions, challengeDone ? 20 : 0);

    private static ClubMemberActivitySummary Summary(
        IReadOnlyList<DayActivity> days,
        IReadOnlyList<ActivityRequirementResult>? requirements = null) =>
        new(days, TotalXp: 0, StreakDays: 0, BoardMissions: 0, BoardClearBonusXp: 0,
            JoinedInPeriod: false, JoinedDateTime: DateTimeOffset.UtcNow, Requirements: requirements);

    [Fact]
    public void NumChallengeDaysDone_CountsDaysWithTheStreakKept()
    {
        var summary = Summary([Day(1, true), Day(2, false), Day(3, true, boardMissions: 2)]);

        summary.NumChallengeDaysDone.Should().Be(2);
    }

    [Fact]
    public void AllRequirementsMet_TrueWithoutRequirements()
    {
        Summary([]).AllRequirementsMet.Should().BeTrue();
    }

    [Fact]
    public void AllRequirementsMet_FalseWhenOneRequirementIsMissed()
    {
        var summary = Summary([], [
            new ActivityRequirementResult(ClubXpActivityKind.DailyChallengeOrDuel, 6, 6, 6),
            new ActivityRequirementResult(ClubXpActivityKind.BoardMission, 1, 2, 2)
        ]);

        summary.AllRequirementsMet.Should().BeFalse();
    }
}

public sealed class ActivityRequirementResultTests
{
    [Fact]
    public void Met_ComparesAgainstTheScaledTarget_NotTheClubsRequirement()
    {
        // A member who joined mid-week needs fewer than the club's 6 days.
        new ActivityRequirementResult(ClubXpActivityKind.DailyChallengeOrDuel, 3, 3, 6).Met.Should().BeTrue();
        new ActivityRequirementResult(ClubXpActivityKind.DailyChallengeOrDuel, 2, 3, 6).Met.Should().BeFalse();
    }

    [Theory]
    [InlineData(ClubXpActivityKind.DailyChallengeOrDuel, "streak")]
    [InlineData(ClubXpActivityKind.BoardMission, "missions")]
    public void Label_NamesTheKindShortly(ClubXpActivityKind kind, string label)
    {
        new ActivityRequirementResult(kind, 0, 1, 1).Label.Should().Be(label);
    }

    [Fact]
    public void Label_IsXp_ForTheRuleXpRequirement()
    {
        new ActivityRequirementResult(null, 0, 80, 80).Label.Should().Be("XP");
    }
}

public sealed class ClubMemberActivityStatusTests
{
    [Fact]
    public void FailedRequirementsText_ListsOnlyTheMissedRequirements()
    {
        var status = new ClubMemberActivityStatus(
            "Alice", "u1", false, 120, 1, false, 0, null, RuleXp: 100,
            Requirements:
            [
                new ActivityRequirementResult(ClubXpActivityKind.DailyChallengeOrDuel, 4, 6, 6),
                new ActivityRequirementResult(ClubXpActivityKind.BoardMission, 2, 2, 2),
                new ActivityRequirementResult(null, 100, 120, 120)
            ]);

        status.FailedRequirementsText.Should().Be("streak 4/6 · XP 100/120");
    }

    [Fact]
    public void FailedRequirementsText_NullWhenEverythingWasMet()
    {
        var status = new ClubMemberActivityStatus(
            "Alice", "u1", true, 120, 0, false, 0, null, RuleXp: 120,
            Requirements: [new ActivityRequirementResult(ClubXpActivityKind.BoardMission, 3, 2, 2)]);

        status.FailedRequirementsText.Should().BeNull();
    }

    [Fact]
    public void RankingXp_PrefersRuleXp_AndFallsBackToRawXp()
    {
        new ClubMemberActivityStatus("A", "u1", true, 220, 0, false, 0, null, RuleXp: 120).RankingXp.Should().Be(120);
        new ClubMemberActivityStatus("A", "u1", true, 220, 0, false, 0, null).RankingXp.Should().Be(220);
    }
}

public sealed class ClubMemberDailyActivityTests
{
    private static readonly Guid Club = Guid.NewGuid();
    private static readonly DateOnly Date = new(2026, 9, 29);

    [Theory]
    [InlineData(1, 0, true)]
    [InlineData(0, 1, false)]
    [InlineData(0, 0, false)]
    public void ExtendsStreak_UsesTheChallengeCount_WhenTracked(int challenges, int legacyMissions, bool expected)
    {
        ClubMemberDailyActivity.Create(Club, "u1", Date, challenges, legacyDailyMissionCount: legacyMissions)
            .ExtendsStreak.Should().Be(expected);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void ExtendsStreak_FallsBackToTheOldDailyMission_WhenTheChallengeWasNotTracked(int legacyMissions, bool expected)
    {
        ClubMemberDailyActivity.Create(Club, "u1", Date, dailyChallengeCount: null, legacyDailyMissionCount: legacyMissions)
            .ExtendsStreak.Should().Be(expected);
    }
}
