using FluentAssertions;
using UseCases.UseCases.CountryChallenges.Configuration;
using UseCases.UseCases.CountryChallenges.Rendering;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.CountryChallenges;

public sealed class ChallengeSettingsTextTests
{
    [Theory]
    [InlineData(false, false, false, "Moving")]
    [InlineData(true, false, false, "No move")]
    [InlineData(true, true, true, "NMPZ")]
    [InlineData(true, true, false, "No move, no pan")]
    [InlineData(false, false, true, "No zoom")]
    public void Mode_NamesTheRestrictionsTheWayPlayersDo(bool moving, bool rotating, bool zooming, string expected)
    {
        ChallengeSettingsText.Mode(new GameSettings(0, moving, rotating, zooming)).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, "no time limit")]
    [InlineData(45, "45 s")]
    [InlineData(60, "1 min")]
    [InlineData(90, "1 min 30 s")]
    [InlineData(600, "10 min")]
    public void TimeLimit_ReadsLikeTheGeoGuessrSettings(int seconds, string expected)
    {
        ChallengeSettingsText.TimeLimit(seconds).Should().Be(expected);
    }

    [Fact]
    public void Describe_CombinesModeAndTime()
    {
        ChallengeSettingsText.Describe(new GameSettings(60, true, true, true)).Should().Be("NMPZ · 1 min");
    }
}
