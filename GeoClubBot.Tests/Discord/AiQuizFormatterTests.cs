using Discord;
using Entities;
using FluentAssertions;
using GeoClubBot.Discord.InputAdapters.Interactions.AI;
using GeoClubBot.Tests.TestBuilders;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Quiz;
using Xunit;

namespace GeoClubBot.Tests.Discord;

/// <summary>The quiz's messages (#491): nothing of the answer shows until it is revealed.</summary>
public sealed class AiQuizFormatterTests
{
    [Fact]
    public void Question_AsksWithoutGivingTheAnswerAway()
    {
        var round = QuizRounds.Round();

        var text = AiQuizFormatter.Question(round);

        text.Should().StartWith("🧩 **Guide quiz** · 🟡 Medium (2 points)\n**Which country is this?**\n");
        text.Should().Contain("one guess each").And.Contain("nobody has guessed yet").And.Contain("<t:");
        text.Should().NotContain("Estonia").And.NotContain("plonkit", "the guide's words and link give the answer away");
    }

    [Theory]
    [InlineData(AiQuizQuestionKind.Region, "**Where in Mongolia was this picture taken?**")]
    [InlineData(AiQuizQuestionKind.Clue, "**What in this picture is typical of Mongolia?**")]
    public void Question_WithinACountry_AsksWhatTheCountryLeavesOpen(AiQuizQuestionKind kind, string asked)
    {
        var text = AiQuizFormatter.Question(QuizRounds.Round(kind, AiQuizDifficulty.Hard, countryName: "Mongolia"));

        text.Should().StartWith($"🧩 **Guide quiz** · Mongolia · 🔴 Hard (3 points)\n{asked}");
    }

    [Fact]
    public void Question_CountsGuessesAndRightOnes()
    {
        var round = QuizRounds.Round();
        round.Guess(2, 1, QuizRounds.AskedAt);
        round.Guess(3, 0, QuizRounds.AskedAt);

        AiQuizFormatter.Question(round).Should().Contain("2 guessed · 1 right");
    }

    [Fact]
    public void Revealed_ShowsTheAnswerTheTallyAndTheGuide()
    {
        var round = QuizRounds.Round();
        round.Guess(2, 1, QuizRounds.AskedAt);
        round.Guess(3, 0, QuizRounds.AskedAt);
        round.Reveal(QuizRounds.AskedAt.AddMinutes(10));

        var text = AiQuizFormatter.Revealed(round);

        text.Should().Contain("✅ **Estonia** · 1 of 2 got it\n> Estonian bollards carry a red reflector on a black band.\n-# PlonkIt › Bollards · <https://www.plonkit.net/estonia#bollards>");
    }

    [Fact]
    public void Verdict_TellsAWrongGuessTheAnswer_AndTheScore()
    {
        var round = QuizRounds.Round();
        var guess = round.Guess(2, 0, QuizRounds.AskedAt)!;
        var outcome = new QuizGuessOutcome(round, guess, new QuizScoreLine(2, 5, 3, 4), new QuizScoreLine(2, 2, 1, 2), 7);

        var text = AiQuizFormatter.Verdict(outcome);

        text.Should().StartWith("❌ Not quite: you picked **Latvia**, it is **Estonia**.\n> Estonian bollards");
        text.Should().EndWith("-# You: 5 points (3 of 4 right) all time · 2 points (1 of 2 right) in the last 7 days. Keep it to yourself until the answer is out!");
    }

    [Fact]
    public void Verdict_ScoresARightGuessByDifficulty()
    {
        var round = QuizRounds.Round(difficulty: AiQuizDifficulty.Hard);
        var guess = round.Guess(2, 1, QuizRounds.AskedAt)!;

        AiQuizFormatter.Verdict(new QuizGuessOutcome(round, guess, new QuizScoreLine(2, 3, 1, 1), new QuizScoreLine(2, 3, 1, 1), 7))
            .Should().StartWith("✅ Right: **Estonia**! +3 points.");
    }

    [Fact]
    public void Buttons_OfferFourAnswers_ThenRevealAndAnother()
    {
        var round = QuizRounds.Round();

        var rows = AiQuizFormatter.Buttons(round).Components.OfType<ActionRowComponent>().ToList();

        rows.Should().HaveCount(2);
        rows[0].Components.OfType<ButtonComponent>().Select(button => button.Label).Should().Equal("Latvia", "Estonia", "Lithuania", "Finland");
        rows[0].Components.OfType<ButtonComponent>().Should().OnlyContain(button => button.Style == ButtonStyle.Secondary && !button.IsDisabled,
            "the right answer must look like the others");
        rows[1].Components.OfType<ButtonComponent>().Select(button => button.Label).Should().Equal("Reveal", "Another");
    }

    [Fact]
    public void Buttons_OnceRevealed_DisableTheAnswers_AndMarkTheRightOne()
    {
        var round = QuizRounds.Round();
        round.Reveal(QuizRounds.AskedAt);

        var rows = AiQuizFormatter.Buttons(round).Components.OfType<ActionRowComponent>().ToList();

        var answers = rows[0].Components.OfType<ButtonComponent>().ToList();
        answers.Should().OnlyContain(button => button.IsDisabled);
        answers[1].Style.Should().Be(ButtonStyle.Success);
        rows[1].Components.OfType<ButtonComponent>().Select(button => button.Label).Should().Equal("Another");
    }

    [Fact]
    public void Leaderboard_SharesRanksOnTies_AndSaysWhereTheMemberStands()
    {
        var board = new QuizLeaderboard(
            [new QuizScoreLine(1, 9, 4, 5), new QuizScoreLine(2, 9, 5, 6), new QuizScoreLine(3, 4, 2, 2)],
            [],
            7,
            (new QuizScoreLine(3, 4, 2, 2), 3),
            null);

        var text = AiQuizFormatter.Leaderboard(board);

        text.Should().Contain("🥇 <@1> · 9 points (4 of 5 right)\n🥇 <@2> · 9 points (5 of 6 right)\n🥉 <@3> · 4 points (2 of 2 right)");
        text.Should().Contain("**Last 7 days**\nNobody has scored yet.");
        text.Should().EndWith("You: #3 with 4 points (2 of 2 right) all time · no answers in the last 7 days");
    }

    [Fact]
    public void AgainId_CarriesTheFilters_WithinDiscordsLimit()
    {
        AiQuizFormatter.AgainId("Vietnam", true, AiQuizDifficulty.Hard).Should().Be("ai-quiz-again:1:3:Vietnam");
        AiQuizFormatter.AgainId(null, false, null).Should().Be("ai-quiz-again:0:0:");
        AiQuizFormatter.AgainId(new string('x', 200), false, null).Length.Should().BeLessThanOrEqualTo(100);
        AiQuizFormatter.AgainId("a:b", false, null).Should().Be("ai-quiz-again:0:0:a b", "a colon would split the id's parts");
    }

    [Theory]
    [InlineData(null, false, null)]
    [InlineData(null, true, AiQuizDifficulty.Easy)]
    [InlineData("Vietnam", false, AiQuizDifficulty.Hard)]
    [InlineData("South Africa", true, null)]
    public void ReadAgainFilters_ReadsBackWhatAgainIdWrote(string? country, bool lesserKnownOnly, AiQuizDifficulty? difficulty)
    {
        var filters = AiQuizFormatter.AgainId(country, lesserKnownOnly, difficulty)["ai-quiz-again:".Length..];

        AiQuizFormatter.ReadAgainFilters(filters).Should().Be((country, lesserKnownOnly, difficulty));
    }

    [Theory]
    [InlineData("1:Vietnam", "Vietnam", true)]
    [InlineData("0:", null, false)]
    public void ReadAgainFilters_StillReadsTheButtonsOfOldMessages(string filters, string? country, bool lesserKnownOnly) =>
        AiQuizFormatter.ReadAgainFilters(filters).Should().Be((country, lesserKnownOnly, (AiQuizDifficulty?)null));

    [Fact]
    public void PickId_ReadsBack()
    {
        var id = Guid.NewGuid();
        var parts = AiQuizFormatter.PickId(id, 3).Split(':');

        parts[0].Should().Be("ai-quiz-pick");
        AiQuizFormatter.ReadPick(parts[1], parts[2]).Should().Be((id, 3));
        AiQuizFormatter.ReadPick("nope", "1").Should().BeNull();
    }
}
