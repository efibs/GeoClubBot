using FluentAssertions;
using UseCases.UseCases.AI.Tools;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// Which questions, asked with a picture, send it to the plant identification service. Each costs one
/// of the day's 500, so a question about bollards does not, however many trees stand in the picture.
/// </summary>
public sealed class PlantIdentificationQuestionTests
{
    [Theory]
    [InlineData("what tree is this?")]
    [InlineData("is this a spruce?")]
    [InlineData("what kind of elm is this")]
    [InlineData("can you identify the leaf on the right")]
    [InlineData("What kind of cactus is this")]
    [InlineData("which plant is that in the background?")]
    [InlineData("can you identify this plant")]
    [InlineData("Identify the tree on the left please")]
    [InlineData("is this a eucalyptus?")]
    [InlineData("Are these palm trees?")]
    [InlineData("what species of pine is in the picture")]
    [InlineData("What are these flowers?")]
    [InlineData("name this bush")]
    [InlineData("what plant is it")]
    [InlineData("Is that an acacia or a mesquite?")]
    [InlineData("what type of vegetation is this")]
    public void Asks_ForAQuestionAboutThePicturesPlant(string question) =>
        PlantIdentificationQuestion.Asks(question).Should().BeTrue();

    [Theory]
    [InlineData("what country is this?")]
    [InlineData("what kind of cone is this?")]
    [InlineData("which region of Brazil has these bollards?")]
    [InlineData("where was this taken")]
    [InlineData("is this Kenya or Uganda?")]
    [InlineData("the trees look tropical, so which country?")]
    [InlineData("tell me about the road markings here")]
    [InlineData("where can I find Khasi pines?")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void DoesNotAsk_ForAnythingElse(string? question) =>
        PlantIdentificationQuestion.Asks(question).Should().BeFalse();

    [Theory]
    [InlineData("it's near Boston though", true)]
    [InlineData("this is Bulgaria btw", true)]
    [InlineData("it's in Bulgaria, does that change the tree?", true)]
    [InlineData("what about the bollards in Bulgaria?", false)]
    [InlineData("which country has these bollards", false)]
    [InlineData("", false)]
    public void ReadsWhetherAFollowUpMayBeAboutThePlant(string followUp, bool expected) =>
        PlantIdentificationQuestion.MayBeAboutThePlant(followUp).Should().Be(expected);
}
