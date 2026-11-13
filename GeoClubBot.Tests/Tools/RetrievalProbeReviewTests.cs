using FluentAssertions;
using GeoClubBot.RetrievalProbe;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using Xunit;

namespace GeoClubBot.Tests.Tools;

/// <summary>
/// The probe's <c>review</c>: it reads a challenge's results as the ApiProbe saved them, and stands in
/// for the database and GeoGuessr with fakes that refuse whatever they were not told to answer.
/// </summary>
public sealed class RetrievalProbeReviewTests
{
    [Fact]
    public async Task ReadsTheApiProbesOutput_RequestLineAndCensusAround()
    {
        var json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "AI", "geoguessr-challenge-highscores-20261113.json"));
        var saved = $"GET https://www.geoguessr.com/api/v3/results/highscores/AbCdEf123456?friends=false\n{json}\n\nfield census\ngame.round  [Number]  present: always\n";

        var highscores = ReviewCommand.Read(saved);

        highscores.Items.Should().HaveCount(2);
        highscores.Items[0].Game.Rounds.Should().HaveCount(5);
        highscores.Items[0].Game.Player.Nick.Should().Be("Player1");
    }

    [Fact]
    public async Task RefusesWhatTheProbeWasNotToldToAnswer()
    {
        var client = ProbeFakes.Answering<IGeoGuessrClient>(new Dictionary<string, Func<object?[], object?>>
        {
            [nameof(IGeoGuessrClient.ReadHighscoresAsync)] = _ => Task.FromResult(new ChallengeResultHighscoresDto { Items = [] })
        });

        (await client.ReadHighscoresAsync("token", new ReadHighscoresQueryParams { Limit = 1, MinRounds = 1 })).Items.Should().BeEmpty();
        Action write = () => client.CreateChallengeAsync(null!);
        write.Should().Throw<InvalidOperationException>().WithMessage("*does not reach IGeoGuessrClient.CreateChallengeAsync*");

        Action challenges = () => ProbeFakes.Refusing<IClubChallengeRepository>().ReadLatestClubChallengeLinksAsync();
        challenges.Should().Throw<InvalidOperationException>();
    }
}
