using FluentAssertions;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Review;
using Xunit;

namespace GeoClubBot.Tests.AI;

public sealed class RoundReviewPromptTests
{
    private static readonly ReviewSubject OwnChallenge = new(ReviewSubjectKind.Challenge, "AbCdEf123456", "Player", IsOwnGame: true);

    [Fact]
    public void Build_WritesEachRound_WhereItWasWhereTheGuessWent_AndWhatItShowed()
    {
        var rounds = new List<ReviewedRound>
        {
            new ReviewedRound(1, "ES", "Valencian Community", "PT", "Faro", 412.4, 3870)
            {
                Evidence = new RoundEvidence(true, [new SeenExcerpt(1, "picture 2, right: yellow reflectors on the guardrail", false), new SeenExcerpt(3, "", true)], [], ["pine trees (picture 1)"])
            },
            new ReviewedRound(2, "TH", null, "TH", "Chiang Mai", 8.26, 4981) { Evidence = new RoundEvidence(true, [], [], []) },
            new ReviewedRound(3, "GH", "Ashanti", null, null, null, 0) { Evidence = new RoundEvidence(true, [], [4, 5], []) },
            new ReviewedRound(4, "BR", null, "BR", null, 1200, 1000) { Evidence = new RoundEvidence(false, [], [6], []) },
            new ReviewedRound(5, null, null, "BR", null, 1200, 1000)
        };

        var text = RoundReviewPrompt.Build(rounds, OwnChallenge, 4500);

        text.Should().StartWith("Review my GeoGuessr game (challenge `AbCdEf123456`), round by round.");
        text.Should().Contain("""
            Round 1: Spain (Valencian Community). Guessed Portugal (Faro), 412 km away, 3,870 points — wrong country.
              Seen in its panorama: [1] picture 2, right: yellow reflectors on the guardrail; [3] (regional).
              Also seen, not from the guides: pine trees (picture 1).
            Round 2: Thailand. Guessed Thailand (Chiang Mai), 8.3 km away, 4,981 points — right country, well played.
              Seen in its panorama: none of the guides' clues.
            Round 3: Ghana (Ashanti). No guess — wrong country.
              Seen in its panorama: none of the guides' clues.
              Not seen in it, but clues of Ghana to look for: [4], [5].
            Round 4: Brazil. Guessed Brazil, 1,200 km away, 1,000 points — right country.
              Its panorama could not be looked at, so nothing is known of what it showed. Clues of Brazil to look for: [6].
            Round 5: an unknown place. Guessed Brazil, 1,200 km away, 1,000 points — right country.
              Its panorama could not be looked at, so nothing is known of what it showed.
            """.ReplaceLineEndings("\n"));
        text.Should().Contain("- Speak to me as \"you\".");
        text.Should().Contain("Say a round showed a clue only when it is listed as seen in it");
        text.Should().Contain("first the one or two easiest, most reliable clues it was seen to show that hold across the whole country");
        text.Should().Contain("Then one regional clue at most.");
        text.Should().Contain("- A round in the right country with 4,500 points or more (\"well played\"): one line.");
        text.Should().Contain("- Every sentence that names a clue either cites a listed excerpt or starts \"Not from the guides:\", the vision model's own notes (\"not from the guides\" above) included. A marker covers only its own sentence.");
        text.Should().EndWith("and end with the two or three things to practise first.");
    }

    [Fact]
    public void Build_SpeaksOfThePlayer_InSomeoneElsesGame()
    {
        var subject = new ReviewSubject(ReviewSubjectKind.Game, "dhmguJ9rQTqI6cmA", "malta", IsOwnGame: false, "A Community World");

        var text = RoundReviewPrompt.Build([new ReviewedRound(1, "GB", null, "GB", null, 5, 4990)], subject, 4500);

        text.Should().StartWith("Review this GeoGuessr game, played by malta on A Community World (malta's game `dhmguJ9rQTqI6cmA`), round by round.");
        text.Should().Contain("This is malta's game, not mine: speak of malta.");
        text.Should().Contain("either cites a listed excerpt or starts \"Not from the guides:\". A marker covers only its own sentence.", "the rule holds whether or not the vision model left notes");
    }

    [Fact]
    public void Build_ExplainsAStreetViewPlace_WithoutAGuessToJudge()
    {
        var place = new ReviewedRound(1, "US", "Texas", null, null, null, null)
        {
            Played = false,
            Evidence = new RoundEvidence(true, [new SeenExcerpt(1, "picture 1: red soil", true)], [], [])
        };

        var text = RoundReviewPrompt.Build([place], new ReviewSubject(ReviewSubjectKind.StreetView, null, null, IsOwnGame: true), 4500);

        text.Should().StartWith("Explain this Street View place: what gives away its country and region. It is in United States (Texas).");
        text.Should().Contain("The place: United States (Texas).\n  Seen in its panorama: [1] (regional) picture 1: red soil.");
        text.Should().Contain("First the clues it was seen to show that tell the country, the easiest and most reliable first");
        text.Should().NotContain("Guessed").And.NotContain("wrong country");
        place.MissedCountry.Should().BeFalse("a place nobody played is nobody's miss");
    }

    [Fact]
    public void Build_AddsGeoNNsReading_AndHowToUseIt_OnlyWhenThereIsOne()
    {
        var read = new ReviewedRound(1, "KE", "Eastern", "RW", "Southern", 832, 3189,
            new PanoramaReading([new CountryProbability("KE", 0.94), new CountryProbability("TR", 0.004)], 65.2, "v2"));

        var text = RoundReviewPrompt.Build([read, new ReviewedRound(2, "GB", null, "GB", null, 5, 4990)], OwnChallenge, 4500);

        text.Should().Contain("Round 1: Kenya (Eastern). Guessed Rwanda (Southern), 832 km away, 3,189 points — wrong country. GeoNN read the panorama as Kenya 94%, Turkey 0% (its guess 65 km off).");
        text.Should().EndWith("never cite it, and never name a clue it did not come with.");
        RoundReviewPrompt.Build([new ReviewedRound(2, "GB", null, "GB", null, 5, 4990)], OwnChallenge, 4500).Should().NotContain("GeoNN");
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
    public void Searches_LookForTheCountrysEasyClues_TheContrastWithTheGuess_AndTheRegion()
    {
        RoundReviewPrompt.Searches(new ReviewedRound(1, "ES", "Valencian Community", "PT", null, 300, 3000)).Should().Equal(
            new RoundSearch("How to recognise Spain: its most reliable clues, seen all over the country", RoundSearchKind.Country),
            new RoundSearch("Spain: road lines, bollards, guardrails and kerbs", RoundSearchKind.Country),
            new RoundSearch("Spain: utility poles, road signs and their script", RoundSearchKind.Country),
            new RoundSearch("How to tell Spain apart from Portugal", RoundSearchKind.Contrast),
            new RoundSearch("How to recognise Valencian Community, Spain: distinctive clues of the region", RoundSearchKind.Region));

        RoundReviewPrompt.Searches(new ReviewedRound(1, "ES", null, "ES", null, 30, 4800)).Should().HaveCount(3, "a right country has nothing to contrast and no region was found");
    }
}
