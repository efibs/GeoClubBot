using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using NSubstitute;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.DailyChallenge;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.DailyChallengeTests;

public sealed class GetCurrentChallengeResultsHandlerTests
{
    private static readonly Guid MainClubId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly IGeoGuessrClientFactory _factory = Substitute.For<IGeoGuessrClientFactory>();
    private readonly IGeoGuessrClient _client = Substitute.For<IGeoGuessrClient>();
    private readonly IClubChallengeRepository _challenges = Substitute.For<IClubChallengeRepository>();

    public GetCurrentChallengeResultsHandlerTests()
    {
        _factory.CreateClient(Arg.Any<Guid>()).Returns(_client);
        _challenges.ReadLatestClubChallengeLinksAsync(Arg.Any<CancellationToken>()).Returns([]);
    }

    private GetCurrentChallengeResultsHandler CreateHandler() =>
        new(_factory, _challenges, new GeoGuessrConfigurationBuilder().WithClub(MainClubId).BuildOptions());

    private static ChallengeResultHighscoresDto Highscores(params (string Id, string Nick, string Score, string Distance)[] players) =>
        new()
        {
            Items = players.Select(p => new ChallengeResultItemDto
            {
                Game = new ChallengeResultGameDto
                {
                    Player = new ChallengeResultPlayerDto
                    {
                        Id = p.Id,
                        Nick = p.Nick,
                        TotalScore = new ChallengeResultPlayerScoreDto { Amount = p.Score, Unit = "points" },
                        TotalDistance = new ChallengeResultPlayerDistanceDto
                        {
                            Meters = new ChallengeResultPlayerDistanceMetersDto { Amount = p.Distance, Unit = "km" }
                        }
                    }
                }
            }).ToList()
        };

    [Fact]
    public async Task Handle_ReturnsEmpty_AndSkipsGeoGuessr_WhenNoActiveChallenges()
    {
        var result = await CreateHandler().Handle(new GetCurrentChallengeResultsQuery(), CancellationToken.None);

        result.Should().BeEmpty();
        _factory.DidNotReceive().CreateClient(Arg.Any<Guid>());
    }

    [Fact]
    public async Task Handle_FetchesHighscoresPerChallenge_OrderedByRolePriority()
    {
        _challenges.ReadLatestClubChallengeLinksAsync(Arg.Any<CancellationToken>()).Returns(
        [
            ClubChallengeLink.Create("Hard", rolePriority: 2, "challenge-hard"),
            ClubChallengeLink.Create("Easy", rolePriority: 1, "challenge-easy")
        ]);
        _client.ReadHighscoresAsync("challenge-easy", Arg.Any<ReadHighscoresQueryParams>(), Arg.Any<CancellationToken>())
            .Returns(Highscores(("p1", "Alice", "5000", "1000")));
        _client.ReadHighscoresAsync("challenge-hard", Arg.Any<ReadHighscoresQueryParams>(), Arg.Any<CancellationToken>())
            .Returns(Highscores(("p2", "Bob", "4000", "2000")));

        var result = await CreateHandler().Handle(new GetCurrentChallengeResultsQuery(), CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].Difficulty.Should().Be("Easy"); // role priority 1 comes first
        result[0].Players.Should().ContainSingle().Which.Nickname.Should().Be("Alice");
        result[1].Difficulty.Should().Be("Hard");
        _factory.Received().CreateClient(MainClubId);
    }
}
