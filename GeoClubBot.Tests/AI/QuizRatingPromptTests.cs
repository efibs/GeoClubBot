using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Quiz;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// What a vision model's rating of a quiz picture is read as (#491). The checks are the point: the quiz
/// once showed flags, labelled maps and pictures that said nothing, and a model asked to judge them is
/// not trusted further than its reply holds together.
/// </summary>
public sealed class QuizRatingPromptTests
{
    private const string FullReply = """
        Country shown: no
        Region shown: no
        Country: medium
        Country wrong: Latvia; Lithuania; Finland
        Region: hard
        Region answer: Saaremaa
        Region wrong: Tartu County; Ida-Viru County; Harju County
        Clue: easy
        Clue answer: the red reflector on the bollard
        Clue wrong: yellow centre line; the snow pole; the concrete kerb
        """;

    [Fact]
    public void Parse_ReadsEveryQuestion_WithTheCountryAsTheTableNamesIt()
    {
        var rating = QuizRatingPrompt.Parse(FullReply, "EE");

        rating.Understood.Should().BeTrue();
        rating.Questions.Should().BeEquivalentTo(new[]
        {
            new RatedQuizQuestion(AiQuizQuestionKind.Country, "Estonia", ["Latvia", "Lithuania", "Finland"], AiQuizDifficulty.Medium),
            new RatedQuizQuestion(AiQuizQuestionKind.Region, "Saaremaa", ["Tartu County", "Ida-Viru County", "Harju County"], AiQuizDifficulty.Hard),
            new RatedQuizQuestion(AiQuizQuestionKind.Clue, "The red reflector on the bollard", ["Yellow centre line", "The snow pole", "The concrete kerb"], AiQuizDifficulty.Easy)
        }, options => options.WithStrictOrdering());
    }

    [Fact]
    public void Parse_DropsTheCountryQuestion_WhenThePictureShowsTheCountry()
    {
        var rating = QuizRatingPrompt.Parse(FullReply.Replace("Country shown: no", "Country shown: yes, the flag"), "EE");

        rating.Questions.Select(question => question.Kind).Should().Equal(AiQuizQuestionKind.Region, AiQuizQuestionKind.Clue);
    }

    [Fact]
    public void Parse_DropsTheRegionQuestion_WhenThePictureNamesTheRegion()
    {
        var rating = QuizRatingPrompt.Parse(FullReply.Replace("Region shown: no", "Region shown: yes (Khovd, Altai)"), "EE");

        rating.Questions.Select(question => question.Kind).Should().Equal(AiQuizQuestionKind.Country, AiQuizQuestionKind.Clue);
    }

    [Fact]
    public void Parse_KeepsTheModelsNo()
    {
        const string reply = """
            Country shown: no
            Region shown: no
            Country: no
            Country wrong: Latvia; Lithuania; Finland
            Region: no
            Clue: no
            """;

        var rating = QuizRatingPrompt.Parse(reply, "EE");

        rating.Understood.Should().BeTrue("a picture that makes no question is a rating, and is not asked about again");
        rating.Questions.Should().BeEmpty();
    }

    [Fact]
    public void Parse_LeavesOutACountryQuestion_WhoseWrongAnswersAreNotThreeOtherCountries()
    {
        var reply = FullReply.Replace("Latvia; Lithuania; Finland", "Latvia; Estonia; Narnia");

        QuizRatingPrompt.Parse(reply, "EE").Questions.Should().NotContain(question => question.Kind == AiQuizQuestionKind.Country);
    }

    [Fact]
    public void Parse_LeavesOutAQuestion_WhoseRightAnswerIsAmongTheWrongOnes()
    {
        var reply = FullReply.Replace("Tartu County; Ida-Viru County", "Saaremaa; Ida-Viru County");

        QuizRatingPrompt.Parse(reply, "EE").Questions.Should().NotContain(question => question.Kind == AiQuizQuestionKind.Region);
    }

    [Fact]
    public void Parse_LeavesOutAnAnswerTooLongForAButton()
    {
        var reply = FullReply.Replace("the red reflector on the bollard", new string('x', QuizRatingPrompt.MaxOptionLength + 1));

        QuizRatingPrompt.Parse(reply, "EE").Questions.Should().NotContain(question => question.Kind == AiQuizQuestionKind.Clue);
    }

    [Fact]
    public void Parse_ReadsMarkdownAndCommas()
    {
        const string reply = """
            - **Country shown:** no
            - **Country:** Hard
            - **Country wrong:** Latvia, Lithuania, Finland
            """;

        QuizRatingPrompt.Parse(reply, "EE").Questions.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new RatedQuizQuestion(AiQuizQuestionKind.Country, "Estonia", ["Latvia", "Lithuania", "Finland"], AiQuizDifficulty.Hard));
    }

    [Theory]
    [InlineData("")]
    [InlineData("I'm sorry, I cannot see the image.")]
    [InlineData("The picture shows a road in Estonia with bollards.")]
    public void Parse_ReadsNoRating_IntoAReplyOfAnotherForm(string reply) =>
        QuizRatingPrompt.Parse(reply, "EE").Understood.Should().BeFalse();

    [Fact]
    public void BuildMessages_GivesTheModelTheGuidesWords_AndThePicture()
    {
        var picture = QuizRounds.Picture("Estonian bollards carry a red reflector on a black band.");

        var messages = QuizRatingPrompt.BuildMessages(picture, "Estonia", "data:image/png;base64,AAAA");

        messages.Should().HaveCount(2);
        messages[1].ToPlainText().Should().Contain("Country: Estonia").And.Contain("Section: Estonia > Bollards")
            .And.Contain("The guide says: Estonian bollards carry a red reflector on a black band.");
        messages[1].Parts.OfType<AiImagePart>().Select(part => part.Url).Should().Equal("data:image/png;base64,AAAA");
    }
}
