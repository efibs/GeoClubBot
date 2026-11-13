using FluentAssertions;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Review;
using Xunit;

namespace GeoClubBot.Tests.AI;

public sealed class RoundReviewPromptTests
{
    [Fact]
    public void Build_WritesEachRound_WhereItWasAndWhereTheGuessWent()
    {
        var rounds = new List<ReviewedRound>
        {
            new(1, "VN", "Lạng Sơn", "CN", "Guangxi", 412.4, 3870),
            new(2, "TH", null, "TH", "Chiang Mai", 8.26, 4981),
            new(3, "GH", "Ashanti", null, null, null, 0),
            new(4, null, null, "BR", null, 1200, 1000)
        };

        var text = RoundReviewPrompt.Build(rounds, "AbCdEf123456");

        text.Should().StartWith("Review my GeoGuessr game (challenge AbCdEf123456), round by round.");
        text.Should().Contain("Round 1: Vietnam (Lạng Sơn). I guessed China (Guangxi), 412 km away, 3,870 points — wrong country.");
        text.Should().Contain("Round 2: Thailand. I guessed Thailand (Chiang Mai), 8.3 km away, 4,981 points — right country.");
        text.Should().Contain("Round 3: Ghana (Ashanti). I made no guess.");
        text.Should().Contain("Round 4: an unknown place. I guessed Brazil, 1,200 km away, 1,000 points — right country.");
        text.Should().EndWith("and end with the two or three things to practise first.");
    }

    [Fact]
    public void Build_AddsGeoNNsReading_AndHowToUseIt_OnlyWhenThereIsOne()
    {
        var read = new ReviewedRound(1, "KE", "Eastern", "RW", "Southern", 832, 3189,
            new PanoramaReading([new CountryProbability("KE", 0.94), new CountryProbability("TR", 0.004)], 65.2, "v2"));

        var text = RoundReviewPrompt.Build([read, new ReviewedRound(2, "GB", null, "GB", null, 5, 4990)], "AbCdEf123456");

        text.Should().Contain("Round 1: Kenya (Eastern). I guessed Rwanda (Southern), 832 km away, 3,189 points — wrong country. GeoNN read the panorama as Kenya 94%, Turkey 0% (its guess 65 km off).");
        text.Should().Contain("Round 2: United Kingdom. I guessed United Kingdom, 5.0 km away, 4,990 points — right country.\n");
        text.Should().EndWith("never cite it, and never name a clue it did not come with.");
        RoundReviewPrompt.Build([new ReviewedRound(2, "GB", null, "GB", null, 5, 4990)], "x").Should().NotContain("GeoNN");
    }

    [Fact]
    public void MissedCountry_IsAGuessInAnotherCountry_OrNone()
    {
        new ReviewedRound(1, "VN", null, "CN", null, 1, 1).MissedCountry.Should().BeTrue();
        new ReviewedRound(1, "VN", null, "vn", null, 1, 1).MissedCountry.Should().BeFalse();
        new ReviewedRound(1, "VN", null, null, null, null, 0).MissedCountry.Should().BeTrue();
        new ReviewedRound(1, null, null, "VN", null, 1, 1).MissedCountry.Should().BeFalse("a round that could not be placed blames nobody");
    }

    [Fact]
    public void SearchText_AsksForTheRegionsClues_WhenThereIsOne()
    {
        RoundReviewPrompt.SearchText(new ReviewedRound(1, "VN", "Lạng Sơn", null, null, null, null))
            .Should().Be("How to recognise Lạng Sơn, Vietnam: distinctive clues of the region");
        RoundReviewPrompt.SearchText(new ReviewedRound(1, "VN", null, null, null, null, null))
            .Should().Be("How to recognise Vietnam: distinctive clues");
    }
}
