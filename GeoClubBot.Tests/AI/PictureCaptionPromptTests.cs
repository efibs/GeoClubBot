using FluentAssertions;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Ingestion;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// What a vision model is asked about a picture with no words of its own, and what is kept of its
/// reply. A generated description is searched and shown as if it were a caption, so what gets in
/// matters: a refusal stored as a caption would be found by every question about refusing.
/// </summary>
public sealed class PictureCaptionPromptTests
{
    [Fact]
    public void BuildMessages_SendsTheTask_ThenWhereThePictureStands_WithThePicture()
    {
        var picture = Picture("A Learnable Colombia", "A Learnable Colombia > Chapter 5: Vegetation", "Colombia");

        var messages = PictureCaptionPrompt.BuildMessages(picture, "data:image/png;base64,AAAA");

        messages.Should().HaveCount(2);
        messages[0].Role.Should().Be(AiChatRole.System);
        messages[0].ToPlainText().Should().Be(PictureCaptionPrompt.SystemPrompt);

        messages[1].Role.Should().Be(AiChatRole.User);
        messages[1].ToPlainText().Should().Be(
            "Picture from the guide \"A Learnable Colombia\", section \"Chapter 5: Vegetation\", country Colombia. Describe it.");
        messages[1].Parts.OfType<AiImagePart>().Select(part => part.Url).Should().Equal("data:image/png;base64,AAAA");
    }

    [Fact]
    public void BuildMessages_AsksPlainly_WhenNothingIsKnownOfThePicture()
    {
        var picture = Picture(title: null, section: null, country: null);

        var messages = PictureCaptionPrompt.BuildMessages(picture, "https://relay/x.png");

        messages[1].ToPlainText().Should().Be("Describe this picture.");
    }

    [Fact]
    public void SystemPrompt_ForbidsGuessingTheCountry_AndAsksForWhatAPlayerRecognisesAPlaceBy()
    {
        // A guessed country would be searched and shown as if the guide had said it.
        PictureCaptionPrompt.SystemPrompt.Should().Contain("Do not guess the country");
        PictureCaptionPrompt.SystemPrompt.Should().ContainAll("bollards", "poles", "licence plates", "map");
        PictureCaptionPrompt.SystemPrompt.Should().Contain("at most 60 words");
    }

    [Theory]
    [InlineData("A two-lane road with a yellow centre line and short white bollards with a red reflector.",
        "A two-lane road with a yellow centre line and short white bollards with a red reflector.")]
    [InlineData("  **A map** of the *Andes*, with the highlands shaded dark green.  ", "A map of the Andes, with the highlands shaded dark green.")]
    [InlineData("This image shows a concrete utility pole with two crossarms.", "A concrete utility pole with two crossarms.")]
    [InlineData("The picture depicts a gravel road.", "A gravel road.")]
    [InlineData("In this photo, a blue sign with white Cyrillic script stands by the road.", "A blue sign with white Cyrillic script stands by the road.")]
    [InlineData("\"A red soil track between eucalyptus trees.\"", "A red soil track between eucalyptus trees.")]
    public void Clean_FlattensMarkdown_AndDropsTheIntroductionThePromptForbids(string reply, string expected) =>
        PictureCaptionPrompt.Clean(reply, 400).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("I'm sorry, but I can't help with identifying locations from images.")]
    [InlineData("I cannot see any image in this conversation.")]
    [InlineData("Unfortunately, no image was provided.")]
    [InlineData("As an AI, I am unable to view pictures.")]
    [InlineData("The image could not be loaded.")]
    [InlineData("safe")]
    public void Clean_KeepsNoReplyThatIsNoDescription(string? reply) =>
        PictureCaptionPrompt.Clean(reply, 400).Should().BeNull();

    [Fact]
    public void Clean_DropsTokenSoup()
    {
        // A provider returned a valid completion of words that change alphabet half-way (2026-11-11).
        var soup = "The roaд has яellow linеs and thе polеs аre concrеte with crossаrms near the tоp of eаch onе.";

        PictureCaptionPrompt.Clean(soup, 400).Should().BeNull();
    }

    [Fact]
    public void Clean_CutsALongReply_AtASentencesEnd()
    {
        var reply = "A paved road with a dashed white centre line. Tall pine trees line both sides of the road. "
                    + "A yellow diamond-shaped warning sign stands on the right, and the sky is overcast.";

        var cleaned = PictureCaptionPrompt.Clean(reply, 100);

        cleaned.Should().Be("A paved road with a dashed white centre line. Tall pine trees line both sides of the road.");
    }

    [Fact]
    public void Clean_CutsAtAWord_WhenNoSentenceEndsLateEnough()
    {
        var reply = new string('x', 10) + " " + string.Join(' ', Enumerable.Repeat("word", 40));

        var cleaned = PictureCaptionPrompt.Clean(reply, 60)!;

        cleaned.Length.Should().BeLessThanOrEqualTo(60);
        cleaned.Should().EndWith("…");
        cleaned.Should().NotContain("  ");
    }

    [Fact]
    public void Clean_CapitalisesAReplyThatStartsLowerCase() =>
        PictureCaptionPrompt.Clean("a gravel road.", 400).Should().Be("A gravel road.");

    private static KnowledgeHit Picture(string? title, string? section, string? country) =>
        new(Guid.NewGuid(), 0f, KnowledgeChunkKind.Image, title ?? "picture", "https://docs.google.com/document/d/x",
            "https://relay/x.png", title, country, section, "Author", 0);
}
