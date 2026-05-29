using Entities;
using FluentAssertions;
using Xunit;

namespace GeoClubBot.Tests.Domain;

public sealed class ClubMemberWeekActivityTests
{
    private static DayMissionStatus Day(int dayOfMonth, bool done) =>
        new(new DateOnly(2025, 1, dayOfMonth), done);

    [Fact]
    public void NumDaysDone_CountsCompletedMissions()
    {
        var activity = new ClubMemberWeekActivity(
            TotalXp: 500,
            DailyMissions: [Day(1, true), Day(2, false), Day(3, true)],
            JoinedThisWeek: false,
            JoinedDateTime: DateTimeOffset.UtcNow);

        activity.NumDaysDone.Should().Be(2);
    }

    [Fact]
    public void AllDaysCompleted_TrueWhenEveryMissionDone()
    {
        var activity = new ClubMemberWeekActivity(
            TotalXp: 0,
            DailyMissions: [Day(1, true), Day(2, true)],
            JoinedThisWeek: false,
            JoinedDateTime: DateTimeOffset.UtcNow);

        activity.AllDaysCompleted.Should().BeTrue();
    }

    [Fact]
    public void AllDaysCompleted_FalseWhenAnyMissionIncomplete()
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
