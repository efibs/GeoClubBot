using Configuration;
using FluentAssertions;
using UseCases.UseCases.DailyMissionReminder;
using Xunit;
using static GeoClubBot.Tests.TestBuilders.MissionBoards;

namespace GeoClubBot.Tests.Application.UseCases.DailyMissionReminderTests;

public sealed class ReminderOutstandingTextTests
{
    private static readonly DateTimeOffset Reset = new(2026, 9, 30, 11, 0, 0, TimeSpan.Zero);

    private static DailyMissionReminderConfiguration Config() => new()
    {
        Schedule = "x",
        DefaultMessage = "Don't forget to {{outstanding_text}}"
    };

    [Fact]
    public void Build_ReturnsNull_WhenNothingIsOutstanding()
    {
        ReminderOutstandingText.Build(new ReminderProgress(true, ReminderMissionState.ClaimedThisCycle), Config())
            .Should().BeNull();
        ReminderOutstandingText.Build(new ReminderProgress(true, ReminderMissionState.NoneFree), Config())
            .Should().BeNull();
        ReminderOutstandingText.Build(new ReminderProgress(true, ReminderMissionState.Unknown), Config())
            .Should().BeNull();
    }

    [Fact]
    public void Build_FillsTheClaimPlaceholders()
    {
        var progress = new ReminderProgress(true, ReminderMissionState.ClaimAvailable, FreeCount: 4, BoardNumber: 3, NextClaimReset: Reset);

        ReminderOutstandingText.Build(progress, Config()).Should().Be(
            $"claim a club mission (4 still free on board 3, the daily claim resets <t:{Reset.ToUnixTimeSeconds()}:R>)!");
    }

    [Fact]
    public void Build_JoinsTheParts_WithTheConfiguredJoiner()
    {
        var config = Config();
        config.Joiner = " + ";
        config.ClaimText = "claim one";

        ReminderOutstandingText.Build(new ReminderProgress(false, ReminderMissionState.ClaimAvailable), config)
            .Should().Be("play the daily challenge (or a duel) + claim one!");
    }

    [Fact]
    public void Build_NamesTheOpenMission()
    {
        var mission = Tile(claimedBy: "u", title: "Do 2 5K's on World", currentProgress: 1);

        ReminderOutstandingText.Build(new ReminderProgress(true, ReminderMissionState.HoldingOpenMission, OpenMission: mission), Config())
            .Should().Be("finish your club mission **Do 2 5K's on World** (1/2) or request help!");
    }

    [Fact]
    public void Build_LeavesOutWhatIsSwitchedOff()
    {
        var config = Config();
        config.RemindChallenge = false;
        config.RemindClaim = false;

        ReminderOutstandingText.Build(new ReminderProgress(false, ReminderMissionState.ClaimAvailable), config)
            .Should().BeNull();
        ReminderOutstandingText.Build(new ReminderProgress(false, ReminderMissionState.Unlinked), config)
            .Should().BeNull();
    }
}

public sealed class MissionBoardConfigurationTests
{
    [Theory]
    // Summer: 12:00 London is 11:00 UTC.
    [InlineData("2026-09-29T10:59:00Z", "2026-09-28T11:00:00Z")]
    [InlineData("2026-09-29T11:00:00Z", "2026-09-29T11:00:00Z")]
    // Winter: 12:00 London is 12:00 UTC.
    [InlineData("2026-11-03T11:30:00Z", "2026-11-02T12:00:00Z")]
    [InlineData("2026-11-03T12:30:00Z", "2026-11-03T12:00:00Z")]
    public void FallbackClaimCycleStart_FollowsLondonThroughDaylightSaving(string now, string expected)
    {
        var config = new MissionBoardConfiguration();

        config.FallbackClaimCycleStart(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture))
            .Should().Be(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }
}
