using System.Text.Json;
using Entities;
using FluentAssertions;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.GeoGuessr.Assemblers;
using Xunit;

namespace GeoClubBot.Tests.Application.OutputPorts;

/// <summary>
/// The board DTOs against a payload shaped exactly like <c>GET /v4/missions/club/board</c>
/// returned it on 2026-09-29 (ids anonymised), deserialized the way Refit does.
/// </summary>
public sealed class ClubMissionBoardAssemblerTests
{
    private const string LiveShapedPayload =
        """
        {
          "periodKey": "2026-09-23T11:00:00.000Z",
          "periodStart": "2026-09-23T11:00:00.0000000Z",
          "periodEnd": "2026-09-30T11:00:00.0000000Z",
          "nextPeriodStart": null,
          "currentBoardNumber": 2,
          "allBoardsCleared": false,
          "boards": [
            {
              "number": 1, "size": 3, "clearRewardXp": 100, "status": "Cleared",
              "clearedAt": "2026-09-23T14:29:06.4020000Z",
              "tiles": [
                {
                  "missionId": "ef69ae7e-ce71-4bc3-9d71-4225d40f8df3", "templateId": "ranked-team-duels-wins",
                  "index": 0, "type": "WinGames", "gameMode": "RankedTeamDuels", "targetProgress": 1,
                  "threshold": 0, "currentProgress": 1, "rewardXp": 20, "claimedBy": "aaaaaaaaaaaaaaaaaaaaaaaa",
                  "claimedAt": "2026-09-23T11:44:49.0010000Z", "helpers": [], "helpRequestedAt": null,
                  "completed": true, "completedAt": "2026-09-23T11:57:08.1150000Z"
                }
              ]
            },
            {
              "number": 2, "size": 4, "clearRewardXp": 100, "status": "Active", "clearedAt": null,
              "tiles": [
                {
                  "missionId": "df39d9ae-72cf-45fc-97ed-84b0d9810455", "templateId": "classic-good-guess",
                  "index": 0, "type": "HighScoreGuesses", "gameMode": "Classic", "mapSlug": "world",
                  "targetProgress": 4, "threshold": 4500, "currentProgress": 2, "rewardXp": 20,
                  "claimedBy": "bbbbbbbbbbbbbbbbbbbbbbbb", "claimedAt": "2026-09-28T11:44:35.2970000Z",
                  "helpers": ["cccccccccccccccccccccccc"], "helpRequestedAt": "2026-09-28T12:00:00.0000000Z",
                  "completed": false, "completedAt": null
                },
                {
                  "missionId": "74ca486d-e2fc-4f51-a55e-e2b98d9a43ff", "templateId": "score-in-classic",
                  "index": 1, "type": "Score", "gameMode": "Classic", "targetProgress": 30000,
                  "threshold": 0, "currentProgress": 0, "rewardXp": 20, "claimedBy": null, "claimedAt": null,
                  "helpers": [], "helpRequestedAt": null, "completed": false, "completedAt": null
                }
              ]
            }
          ],
          "templates": {
            "ranked-team-duels-wins": { "id": "ranked-team-duels-wins", "title": "Win a ranked Team Duel", "titlePlural": "Win {0} ranked Team Duels", "description": null, "mapName": null, "iconPath": "i.webp", "artPath": "a.webp" },
            "classic-good-guess": { "id": "classic-good-guess", "title": "Score 4500 or more on a guess on {1}", "titlePlural": "Score 4500 or more on {0} guesses on {1}", "description": "Can be done over multiple games", "mapName": "World", "iconPath": "i.webp", "artPath": "a.webp" },
            "score-in-classic": { "id": "score-in-classic", "title": "Score {0} point on a Classic Map", "titlePlural": "Score {0} points on a Classic Map", "description": null, "mapName": null, "iconPath": "i.webp", "artPath": "a.webp" }
          },
          "you": {
            "claimedMissionIds": [], "helpingMissionIds": [], "progressByMission": { "df39d9ae-72cf-45fc-97ed-84b0d9810455": 2 },
            "canClaim": false, "canHelp": true, "nextDayAt": "2026-09-29T11:00:00.0000000Z"
          }
        }
        """;

    private static ClubMissionBoardSnapshotDto Deserialize() =>
        JsonSerializer.Deserialize<ClubMissionBoardSnapshotDto>(LiveShapedPayload, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    [Fact]
    public void TheDtos_ReadEveryFieldTheBotUses()
    {
        var dto = Deserialize();

        dto.PeriodStart.Should().Be(new DateTimeOffset(2026, 9, 23, 11, 0, 0, TimeSpan.Zero));
        dto.CurrentBoardNumber.Should().Be(2);
        dto.Boards.Should().HaveCount(2);
        dto.Templates.Should().ContainKey("classic-good-guess");
        dto.You!.NextDayAt.Should().Be(new DateTimeOffset(2026, 9, 29, 11, 0, 0, TimeSpan.Zero));

        var open = dto.Boards[1].Tiles[0];
        open.ClaimedBy.Should().Be("bbbbbbbbbbbbbbbbbbbbbbbb");
        open.Helpers.Should().Equal("cccccccccccccccccccccccc");
        open.HelpRequestedAt.Should().NotBeNull();
        open.Threshold.Should().Be(4500);
    }

    [Fact]
    public void AssembleEntity_BuildsTheBoardWeek_WithRenderedTitles()
    {
        var week = ClubMissionBoardAssembler.AssembleEntity(Deserialize());

        week.CurrentBoard!.Number.Should().Be(2);
        week.NextClaimResetAt.Should().Be(new DateTimeOffset(2026, 9, 29, 11, 0, 0, TimeSpan.Zero));
        week.AllTiles.Select(t => t.Title).Should().Equal(
            "Win a ranked Team Duel",
            "Score 4500 or more on 4 guesses on World",
            "Score 30000 points on a Classic Map");
        week.FreeTiles.Should().ContainSingle();
        week.OpenClaims.Should().ContainSingle().Which.HelpRequested.Should().BeTrue();
        week.HelpedBy("cccccccccccccccccccccccc").Should().ContainSingle();
        week.OpenClaims[0].BoardNumber.Should().Be(2);
    }

    [Theory]
    [InlineData(1, "Win a Ranked Duel")]
    [InlineData(3, "Win 3 Ranked Duels")]
    public void TitleRenderer_UsesThePluralUnlessTheTargetIsOne(int target, string expected)
    {
        var template = new ClubMissionTemplateDto { Id = "t", Title = "Win a Ranked Duel", TitlePlural = "Win {0} Ranked Duels" };
        var tile = new ClubMissionTileDto { MissionId = Guid.NewGuid(), TemplateId = "t", TargetProgress = target };

        ClubMissionTitleRenderer.Render(tile, template).Should().Be(expected);
    }

    [Fact]
    public void TitleRenderer_FillsTheMapAndThreshold()
    {
        var template = new ClubMissionTemplateDto
        {
            Id = "t",
            Title = "x",
            TitlePlural = "Get {0} games above {2} points on {1}",
            MapName = "A Moving World"
        };
        var tile = new ClubMissionTileDto { MissionId = Guid.NewGuid(), TemplateId = "t", TargetProgress = 2, Threshold = 18500 };

        ClubMissionTitleRenderer.Render(tile, template).Should().Be("Get 2 games above 18500 points on A Moving World");
    }

    [Fact]
    public void TitleRenderer_FallsBack_WhenTheTemplateIsMissing()
    {
        var tile = new ClubMissionTileDto { MissionId = Guid.NewGuid(), TemplateId = "new-thing", Type = "WinGames", TargetProgress = 2 };

        ClubMissionTitleRenderer.Render(tile, null).Should().Be("WinGames (2)");
    }
}
