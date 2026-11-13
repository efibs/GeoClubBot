using Discord;
using FluentAssertions;
using GeoClubBot.Discord.InputAdapters.Interactions.AI;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Quiz;
using Xunit;

namespace GeoClubBot.Tests.Discord;

/// <summary>The quiz message: everything that gives the answer away sits inside one spoiler.</summary>
public sealed class AiQuizFormatterTests
{
    [Fact]
    public void Question_HidesCountryGuideAnswerAndSource_InOneSpoiler()
    {
        var question = new QuizQuestion(Clue(), "Vietnam", "Vietnam region guide", "Poles",
            "In Lạng Sơn the poles carry a white band at eye height.");

        var text = AiQuizFormatter.Question(question, askedCountry: null);

        text.Should().StartWith("🧩 **Guide quiz**\nWhere is this, and what gives it away? Decide, then open the spoiler.\n\n||");
        text.Should().EndWith("||");
        text.Should().Contain("**Vietnam** · Vietnam region guide › Poles\nIn Lạng Sơn the poles carry a white band at eye height.\nSource: <https://docs.google.com/document/d/x#heading=h.1>");
        text.IndexOf("||", StringComparison.Ordinal).Should().BeLessThan(text.IndexOf("Vietnam", StringComparison.Ordinal), "the country is inside the spoiler");
    }

    [Fact]
    public void Question_NamesTheCountryAsked_OutsideTheSpoiler_AndNotTheClue()
    {
        var question = new QuizQuestion(Clue(), "Vietnam", "Vietnam region guide", "", "A white band on the pole.");

        var text = AiQuizFormatter.Question(question, askedCountry: "vietnam");

        text.Should().StartWith("🧩 **Guide quiz** · Vietnam\nWhat does this picture tell you?");
    }

    [Fact]
    public void Question_NeverLetsAGuidesOwnBarsCloseTheSpoiler()
    {
        var question = new QuizQuestion(Clue(), null, "Guide || tricks", "", "A || B");

        var text = AiQuizFormatter.Question(question, null);

        text.Split("||").Should().HaveCount(3, "exactly one spoiler: an opening and a closing pair");
    }

    [Fact]
    public void Score_SaysTheStreakAndTheTally()
    {
        AiQuizFormatter.Score(new QuizScore(3, 5, 7, 9), right: true).Should().Be("Nice one! Streak: 3 🔥 · 7 of 9 right since the last restart · best streak 5");
        AiQuizFormatter.Score(new QuizScore(0, 1, 1, 2), right: false).Should().Be("Next time. No streak · 1 of 2 right since the last restart");
    }

    [Fact]
    public void AgainId_CarriesTheFilters_WithinDiscordsLimit()
    {
        AiQuizFormatter.AgainId("Vietnam", true).Should().Be("ai-quiz-again:1:Vietnam");
        AiQuizFormatter.AgainId(null, false).Should().Be("ai-quiz-again:0:");
        AiQuizFormatter.AgainId(new string('x', 200), false).Length.Should().BeLessThanOrEqualTo(100);
        AiQuizFormatter.AgainId("a:b", false).Should().Be("ai-quiz-again:0:a b", "a colon would split the id's parts");
    }

    [Fact]
    public void Buttons_OfferJudgingAndAnother() =>
        AiQuizFormatter.Buttons("Vietnam", false).Components.Should().ContainSingle()
            .Which.Should().BeOfType<ActionRowComponent>()
            .Which.Components.Should().HaveCount(3);

    private static KnowledgeHit Clue() =>
        new(Guid.NewGuid(), 0f, KnowledgeChunkKind.Image, "text", "https://docs.google.com/document/d/x#heading=h.1",
            "https://bot.example/api/v1/ai/images/abc.jpg", "Vietnam region guide", "vietnam", "Vietnam region guide > Poles", null, 0);
}
