using System.Text.Json;
using FluentAssertions;
using UseCases.OutputPorts.GeoGuessr;
using Xunit;

namespace GeoClubBot.Tests.Application.OutputPorts;

/// <summary>
/// The highscore DTOs against a payload shaped like <c>GET /v3/results/highscores/{challengeId}</c>
/// returned it on 2026-10-22 (trimmed, ids anonymised), deserialized the way Refit does. GeoGuessr
/// had just replaced <c>totalScore: { amount, unit }</c> with a plain <c>totalScoreInPoints</c>, which
/// broke every result read until the DTO followed (#391).
/// </summary>
public sealed class ChallengeResultHighscoresDtoTests
{
    private const string LiveShapedPayload =
        """
        {
          "items": [
            {
              "game": {
                "token": "HJlEzt0deVH9IkG4",
                "type": "challenge",
                "state": "finished",
                "roundCount": 5,
                "player": {
                  "totalScoreInPoints": 16689,
                  "totalDistance": {
                    "meters": { "amount": "1198.8", "unit": "km" },
                    "miles": { "amount": "744.9", "unit": "miles" }
                  },
                  "totalDistanceInMeters": 1198794.9175285,
                  "totalTime": 263,
                  "id": "aaaaaaaaaaaaaaaaaaaaaaaa",
                  "nick": "Alice",
                  "club": { "tag": "DRGN", "clubId": "6372d694-e433-4f69-8f7c-592ec127a577", "level": 37 },
                  "countryCode": "de"
                }
              },
              "isIncognito": false
            }
          ],
          "paginationToken": null
        }
        """;

    [Fact]
    public void Deserialize_ReadsTheScoreFromTotalScoreInPoints()
    {
        var dto = JsonSerializer.Deserialize<ChallengeResultHighscoresDto>(
            LiveShapedPayload, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        var player = dto.Items.Should().ContainSingle().Subject.Game.Player;
        player.Id.Should().Be("aaaaaaaaaaaaaaaaaaaaaaaa");
        player.Nick.Should().Be("Alice");
        player.TotalScoreInPoints.Should().Be(16689);
        player.TotalDistance.Meters.Amount.Should().Be("1198.8");
        player.TotalDistance.Meters.Unit.Should().Be("km");
    }
}
