using Constants;
using FluentAssertions;
using GeoClubBot.MockGeoGuessr.Client;
using GeoClubBot.MockGeoGuessr.DataStore;
using UseCases.OutputPorts.GeoGuessr;
using Xunit;

namespace GeoClubBot.Tests.Tools;

/// <summary>
/// The mock GeoGuessr stands in for the real API during local development, so its challenges must look
/// like real ones to the bot — otherwise the daily and country challenges fail locally for reasons that
/// never happen in production.
/// </summary>
public sealed class MockGeoGuessrChallengeTests
{
    private readonly MockGeoGuessrDataStore _store = new();

    [Fact]
    public void ChallengeTokens_FitTheColumnTheBotStoresThemIn_AndDoNotRepeat()
    {
        var tokens = Enumerable.Range(0, 1000).Select(_ => _store.GenerateChallengeToken()).ToList();

        tokens.Should().OnlyContain(t => t.Length == StringLengthConstants.GeoGuessrChallengeIdLength);
        tokens.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task ReadHighscoresAsync_ReturnsTheBestScoreFirst_LikeGeoGuessr()
    {
        var client = new MockGeoGuessrClient(_store);
        var token = (await client.CreateChallengeAsync(new PostChallengeRequestDto
        {
            AccessLevel = 1,
            ChallengeType = 0,
            ForbidMoving = false,
            ForbidRotating = false,
            ForbidZooming = false,
            Map = "world",
            TimeLimit = 60
        })).Token;

        foreach (var (player, score) in new[] { ("second", 18000), ("first", 24000), ("third", 9000), ("fourth", 400) })
        {
            _store.ChallengeHighscores[token].Add(Score(player, score));
        }

        var highscores = await client.ReadHighscoresAsync(token, new ReadHighscoresQueryParams { Limit = 3, MinRounds = 5 });

        highscores.Items.Select(i => i.Game.Player.Id).Should().Equal("first", "second", "third");
    }

    private static ChallengeResultItemDto Score(string player, int score) => new()
    {
        Game = new ChallengeResultGameDto
        {
            Player = new ChallengeResultPlayerDto
            {
                Id = player,
                Nick = player,
                TotalScore = new ChallengeResultPlayerScoreDto { Amount = score.ToString(), Unit = "points" },
                TotalDistance = new ChallengeResultPlayerDistanceDto
                {
                    Meters = new ChallengeResultPlayerDistanceMetersDto { Amount = "100", Unit = "km" }
                }
            }
        }
    };
}
