using Configuration;
using Entities;
using FluentAssertions;
using UseCases.UseCases.MissionBoard;
using Xunit;
using static GeoClubBot.Tests.TestBuilders.MissionBoards;

namespace GeoClubBot.Tests.Application.UseCases.MissionBoard;

public sealed class StuckMissionDetectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 18, 0, 0, TimeSpan.Zero);

    private static MissionBoardAlertsConfiguration Options(int lastMissionsThreshold = 0) => new()
    {
        Schedule = "x",
        OpenClaimAlertAfter = TimeSpan.FromHours(6),
        HelpRequestAlertAfter = TimeSpan.FromHours(1),
        LastMissionsThreshold = lastMissionsThreshold
    };

    [Fact]
    public void Find_ReportsMissionsClaimedLongAgo_AndOldHelpRequests()
    {
        var old = Tile(index: 0, claimedBy: "a", claimedAt: Now.AddHours(-7));
        var fresh = Tile(index: 1, claimedBy: "b", claimedAt: Now.AddHours(-2));
        var help = Tile(index: 2, claimedBy: "c", claimedAt: Now.AddHours(-3), helpRequestedAt: Now.AddHours(-2));
        var done = Tile(index: 3, claimedBy: "d", claimedAt: Now.AddDays(-2), completed: true);
        var week = Week(Board(1, [old, fresh, help, done, .. FreeTiles(5)]));

        var stuck = StuckMissionDetector.Find(week, Now, Options());

        stuck.Should().BeEquivalentTo(new[]
        {
            new StuckMission(old, MissionBoardAlertKind.OpenClaim),
            new StuckMission(help, MissionBoardAlertKind.HelpRequest)
        });
    }

    [Fact]
    public void Find_WaitsForTheLastMissions_WhenAThresholdIsSet()
    {
        var old = Tile(index: 0, claimedBy: "a", claimedAt: Now.AddHours(-7));

        StuckMissionDetector.Find(Week(Board(1, [old, .. FreeTiles(8)])), Now, Options(lastMissionsThreshold: 3))
            .Should().BeEmpty("eight other missions are still open");

        var nearlyDone = Enumerable.Range(1, 7).Select(i => Tile(index: i, claimedBy: $"x{i}", completed: true));
        StuckMissionDetector.Find(Week(Board(1, [old, .. nearlyDone, Tile(index: 8)])), Now, Options(lastMissionsThreshold: 3))
            .Should().ContainSingle();
    }

    [Fact]
    public void Find_SkipsAKindOfAlert_ThatIsSwitchedOff()
    {
        var options = Options();
        options.OpenClaimAlertAfter = null;
        var tile = Tile(claimedBy: "a", claimedAt: Now.AddDays(-1), helpRequestedAt: Now.AddDays(-1));

        StuckMissionDetector.Find(Week(Board(1, tile)), Now, options)
            .Should().ContainSingle().Which.Kind.Should().Be(MissionBoardAlertKind.HelpRequest);
    }

    [Fact]
    public void Render_FillsEveryPlaceholder()
    {
        var tile = Tile(boardNumber: 3, claimedBy: "a", claimedAt: Now.AddHours(-7), title: "Win 2 Ranked Duels", currentProgress: 1);
        var week = Week(Board(3, [tile, .. FreeTiles(3, boardNumber: 3)]));

        var text = CheckStuckMissionsHandler.Render(
            "{{claimer}}|{{mission}}|{{progress}}|{{claimed_at}}|{{board}}|{{remaining}}|{{club}}",
            tile, week, "**Alice**", "Dragon");

        text.Should().Be($"**Alice**|Win 2 Ranked Duels|1/2|<t:{Now.AddHours(-7).ToUnixTimeSeconds()}:R>|3|4|Dragon");
    }

    [Theory]
    [InlineData(MissionBoardAlertKind.OpenClaim)]
    [InlineData(MissionBoardAlertKind.HelpRequest)]
    public void Render_DefaultChannelAlerts_LeadWithTheClub(MissionBoardAlertKind kind)
    {
        // Every club's alerts share one channel and every club has the same missions, so an alert
        // that names its club only in passing is easily read as the other club's.
        var options = Options();
        var tile = Tile(claimedBy: "a", claimedAt: Now.AddHours(-7));
        var template = kind == MissionBoardAlertKind.HelpRequest ? options.HelpRequestMessage : options.OpenClaimMessage;

        var text = CheckStuckMissionsHandler.Render(template, tile, Week(Board(1, tile)), "**Alice**", "Dragon's Den");

        text.Should().MatchRegex(@"^\S+ \*\*\[Dragon's Den\]\*\* \*\*Alice\*\* ");
    }
}
