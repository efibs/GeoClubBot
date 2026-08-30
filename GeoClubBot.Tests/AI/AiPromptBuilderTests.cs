using Entities;
using FluentAssertions;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Conversations;
using Xunit;

namespace GeoClubBot.Tests.AI;

public sealed class AiPromptBuilderTests
{
    [Fact]
    public void Build_LeadsWithTheSystemPrompt_AndEndsWithTheQuestion()
    {
        var (messages, _, _) = AiPromptBuilder.Build(ConversationContext.Empty, "what is this?", [], []);

        messages[0].Role.Should().Be(AiChatRole.System);
        messages[^1].Role.Should().Be(AiChatRole.User);
        messages[^1].ToPlainText().Should().Contain("Question: what is this?");
    }

    [Fact]
    public void Build_AttributesEachUserTurn()
    {
        // Several people can share one branch. Without attribution the model reads a group discussion
        // as a single person contradicting themselves.
        var context = new ConversationContext(
        [
            new ConversationTurnView(AiTurnRole.User, 42, "first question", []),
            new ConversationTurnView(AiTurnRole.Assistant, 1, "an answer", []),
            new ConversationTurnView(AiTurnRole.User, 99, "second question", [])
        ], WasTrimmed: false, ParentDepth: 2);

        var (messages, _, _) = AiPromptBuilder.Build(context, "third question", [], []);

        messages.Should().HaveCount(5, "system prompt, three replayed turns, and the new question");
        messages[1].ToPlainText().Should().Contain("<@42>").And.Contain("first question");
        messages[2].Role.Should().Be(AiChatRole.Assistant);
        messages[3].ToPlainText().Should().Contain("<@99>");
    }

    [Fact]
    public void Build_TellsTheModel_WhenHistoryWasTrimmed()
    {
        var context = new ConversationContext([], WasTrimmed: true, ParentDepth: 5);

        var (messages, _, _) = AiPromptBuilder.Build(context, "question", [], []);

        messages.Should().Contain(m => m.Role == AiChatRole.System && m.ToPlainText().Contains("trimmed"));
    }

    [Fact]
    public void Build_NumbersExcerpts_AndMarksImagesSeparately()
    {
        var (messages, images, _) = AiPromptBuilder.Build(
            ConversationContext.Empty,
            "area codes?",
            [],
            [
                Hit(KnowledgeChunkKind.Text, "Tunisian codes run 70-79.", country: "tunisia"),
                Hit(KnowledgeChunkKind.Image, "map of area codes", imageUrl: "https://i.imgur.com/map.png")
            ]);

        var prompt = messages[^1].ToPlainText();
        prompt.Should().Contain("[1] Tunisia").And.Contain("Tunisian codes run 70-79.");
        prompt.Should().Contain("[image 2]");

        images.Should().ContainSingle();
        images[0].Marker.Should().Be(2, "markers are shared with the text excerpts so citations are unambiguous");
        images[0].ImageUrl.Should().Be("https://i.imgur.com/map.png");
    }

    [Fact]
    public void Build_ListsTheAttachableImages_NextToTheQuestion()
    {
        // The markers are already in the excerpt labels, but a model that only reads them there tends
        // to treat them as formatting and answer "I can't display images" — the observed failure. The
        // roster restates them at the point of answering as something it can actually do.
        var (messages, _, _) = AiPromptBuilder.Build(
            ConversationContext.Empty,
            "what does the MR9 look like?",
            [],
            [
                Hit(KnowledgeChunkKind.Text, "The MR9 runs through the centre south."),
                Hit(KnowledgeChunkKind.Image, "wooded hills", imageUrl: "https://relay/a.png"),
                Hit(KnowledgeChunkKind.Image, "dirt tracks", imageUrl: "https://relay/b.png")
            ]);

        var prompt = messages[^1].ToPlainText();
        prompt.Should().Contain("Images you can attach: [image 2], [image 3]");
        prompt.IndexOf("Images you can attach", StringComparison.Ordinal)
            .Should().BeLessThan(prompt.IndexOf("Question:", StringComparison.Ordinal),
                "it has to be the last thing read before the question");
    }

    [Fact]
    public void Build_OmitsTheImageRoster_WhenNothingRetrievedHasAPicture()
    {
        var (messages, _, _) = AiPromptBuilder.Build(
            ConversationContext.Empty,
            "what does the MR9 look like?",
            [],
            [Hit(KnowledgeChunkKind.Text, "The MR9 runs through the centre south.")]);

        messages[^1].ToPlainText().Should().NotContain("Images you can attach");
    }

    [Fact]
    public void SystemPrompt_TellsTheModelItCanShowImages()
    {
        // Pinned because it is the whole point of indexing images: a model that believes it cannot
        // display them writes a perfectly good answer and silently drops the picture.
        AiPromptBuilder.SystemPrompt.Should().Contain("You can show pictures");
        AiPromptBuilder.SystemPrompt.Should().Contain("Never say you are unable to display");
    }

    [Fact]
    public void Build_DoesNotRepeatTheCountry_WhenTheSectionPathAlreadyNamesIt()
    {
        // Plonk It section paths are rooted at the country, so prefixing one produces
        // "Eswatini · Eswatini > Spotlight" — which reads as a bug in the citation, not as emphasis.
        var (_, _, excerpts) = AiPromptBuilder.Build(
            ConversationContext.Empty,
            "question",
            [],
            [Hit(KnowledgeChunkKind.Text, "text", country: "tunisia")]);

        excerpts.Should().ContainSingle();
        excerpts[0].Label.Should().Be("Tunisia > Identifying");
    }

    [Fact]
    public void Build_SaysSoExplicitly_WhenNothingWasRetrieved()
    {
        // Otherwise the model quietly invents guide content it was never given.
        var (messages, _, _) = AiPromptBuilder.Build(ConversationContext.Empty, "question", [], []);

        messages[^1].ToPlainText().Should().Contain("No guide excerpts matched");
    }

    [Fact]
    public void Build_CarriesAttachedImagesOnTheQuestion()
    {
        var (messages, _, _) = AiPromptBuilder.Build(
            ConversationContext.Empty, "what is this?", ["https://cdn/x.png"], []);

        messages[^1].Parts.OfType<AiImagePart>().Select(p => p.Url).Should().Equal("https://cdn/x.png");
    }

    [Fact]
    public void ResolveCitedImages_ReturnsCitedImages_AndStripsTheMarkers()
    {
        // The marker is an instruction to us, not text the reader should see.
        var available = new[] { Image(2, "https://i.imgur.com/a.png"), Image(3, "https://i.imgur.com/b.png") };

        var (text, cited) = AiPromptBuilder.ResolveCitedImages(
            "The codes are shown here [image 3] clearly.", available, maxImages: 3);

        text.Should().Be("The codes are shown here  clearly.");
        cited.Should().ContainSingle().Which.ImageUrl.Should().Be("https://i.imgur.com/b.png");
    }

    [Fact]
    public void ResolveCitedImages_ReturnsThemInCitationOrder()
    {
        var available = new[] { Image(1, "https://a"), Image(2, "https://b") };

        var (_, cited) = AiPromptBuilder.ResolveCitedImages("see [image 2] then [image 1]", available, maxImages: 3);

        cited.Select(i => i.ImageUrl).Should().Equal("https://b", "https://a");
    }

    [Fact]
    public void ResolveCitedImages_ShowsARepeatedCitationOnce()
    {
        var available = new[] { Image(1, "https://a") };

        var (_, cited) = AiPromptBuilder.ResolveCitedImages("[image 1] and again [image 1]", available, maxImages: 3);

        cited.Should().ContainSingle();
    }

    [Fact]
    public void ResolveCitedImages_StopsAtTheReplyLimit()
    {
        var available = new[] { Image(1, "https://a"), Image(2, "https://b"), Image(3, "https://c") };

        var (_, cited) = AiPromptBuilder.ResolveCitedImages(
            "[image 1] [image 2] [image 3]", available, maxImages: 2);

        cited.Should().HaveCount(2);
    }

    [Fact]
    public void ResolveCitedImages_IgnoresAMarkerThatWasNeverOffered()
    {
        // Models hallucinate citations; an invented marker must not crash or attach a wrong image.
        var available = new[] { Image(1, "https://a") };

        var (text, cited) = AiPromptBuilder.ResolveCitedImages("see [image 9]", available, maxImages: 3);

        cited.Should().BeEmpty();
        text.Should().Be("see");
    }

    [Fact]
    public void ResolveCitedImages_StillStripsMarkers_WhenImagesAreDisabled()
    {
        var (text, cited) = AiPromptBuilder.ResolveCitedImages(
            "look [image 1] here", [Image(1, "https://a")], maxImages: 0);

        text.Should().Be("look  here");
        cited.Should().BeEmpty();
    }

    private static CitedImage Image(int marker, string url) => new(marker, url, "https://source", "Title");

    private static KnowledgeHit Hit(
        KnowledgeChunkKind kind,
        string text,
        string? imageUrl = null,
        string? country = null) =>
        new(Guid.NewGuid(), 0.9f, kind, text, "https://www.plonkit.net/tunisia", imageUrl,
            "Tunisia", country, "Tunisia > Identifying", "Plonk It team", 0);
}
