using Entities;
using FluentAssertions;
using Xunit;

namespace GeoClubBot.Tests.Domain;

public sealed class ClubMemberWeekActivityTests
{
    // A day is only "done" when both of its club-XP awards were earned; challenge defaults to
    // matching the mission so the all-or-nothing cases below stay readable.
    private static DayMissionStatus Day(int dayOfMonth, bool done, bool? challengeDone = null) =>
        new(new DateOnly(2025, 1, dayOfMonth), done, challengeDone ?? done);

    [Fact]
    public void NumDaysDone_CountsOnlyDaysWithBothAwards()
    {
        var activity = new ClubMemberWeekActivity(
            TotalXp: 500,
            DailyMissions:
            [
                Day(1, true),
                Day(2, false),
                Day(3, true),
                Day(4, done: true, challengeDone: false)
            ],
            JoinedThisWeek: false,
            JoinedDateTime: DateTimeOffset.UtcNow);

        activity.NumDaysDone.Should().Be(2);
    }

    [Fact]
    public void PerAwardCounts_AreTrackedIndependently()
    {
        var activity = new ClubMemberWeekActivity(
            TotalXp: 500,
            DailyMissions:
            [
                Day(1, done: true, challengeDone: false),
                Day(2, done: false, challengeDone: true),
                Day(3, done: true, challengeDone: true)
            ],
            JoinedThisWeek: false,
            JoinedDateTime: DateTimeOffset.UtcNow);

        activity.NumMissionDaysDone.Should().Be(2);
        activity.NumChallengeDaysDone.Should().Be(2);
        activity.NumDaysDone.Should().Be(1);
    }

    [Fact]
    public void AllDaysCompleted_FalseWhenOnlyOneOfTheTwoAwardsIsEarned()
    {
        var activity = new ClubMemberWeekActivity(
            TotalXp: 0,
            DailyMissions: [Day(1, true), Day(2, done: true, challengeDone: false)],
            JoinedThisWeek: false,
            JoinedDateTime: DateTimeOffset.UtcNow);

        activity.AllDaysCompleted.Should().BeFalse();
    }

    [Fact]
    public void AllDaysCompleted_TrueWhenEveryDayHasBothAwards()
    {
        var activity = new ClubMemberWeekActivity(
            TotalXp: 0,
            DailyMissions: [Day(1, true), Day(2, true)],
            JoinedThisWeek: false,
            JoinedDateTime: DateTimeOffset.UtcNow);

        activity.AllDaysCompleted.Should().BeTrue();
    }

    [Fact]
    public void AllDaysCompleted_FalseWhenAnyDayIsIncomplete()
    {
        var activity = new ClubMemberWeekActivity(
            TotalXp: 0,
            DailyMissions: [Day(1, true), Day(2, false)],
            JoinedThisWeek: false,
            JoinedDateTime: DateTimeOffset.UtcNow);

        activity.AllDaysCompleted.Should().BeFalse();
    }

    [Fact]
    public void AllDaysCompleted_FalseWhenNoMissions()
    {
        // Guard: All() on an empty sequence is vacuously true, so the count check matters.
        var activity = new ClubMemberWeekActivity(
            TotalXp: 0,
            DailyMissions: [],
            JoinedThisWeek: true,
            JoinedDateTime: DateTimeOffset.UtcNow);

        activity.AllDaysCompleted.Should().BeFalse();
        activity.NumDaysDone.Should().Be(0);
    }
}
