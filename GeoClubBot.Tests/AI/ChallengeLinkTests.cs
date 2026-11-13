using FluentAssertions;
using UseCases.UseCases.AI.Review;
using Xunit;

namespace GeoClubBot.Tests.AI;

public sealed class ChallengeLinkTests
{
    [Theory]
    [InlineData("https://www.geoguessr.com/challenge/AbCdEf123456", "AbCdEf123456")]
    [InlineData("<https://www.geoguessr.com/challenge/AbCdEf123456>", "AbCdEf123456")]
    [InlineData("https://www.geoguessr.com/results/AbCdEf123456", "AbCdEf123456")]
    [InlineData("https://www.geoguessr.com/de/challenge/AbCdEf123456?utm=x", "AbCdEf123456")]
    [InlineData("geoguessr.com/challenge/AbCdEf123456", "AbCdEf123456")]
    [InlineData("AbCdEf123456", "AbCdEf123456")]
    [InlineData("  AbCdEf123456  ", "AbCdEf123456")]
    public void ReadsTheToken_FromALinkOrOnItsOwn(string text, string expected) =>
        ChallengeLink.TryReadToken(text).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("today's daily")]
    [InlineData("https://www.geoguessr.com/maps/world")]
    [InlineData("abc")]
    public void ReadsNothing_FromAnythingElse(string? text) =>
        ChallengeLink.TryReadToken(text).Should().BeNull();
}
