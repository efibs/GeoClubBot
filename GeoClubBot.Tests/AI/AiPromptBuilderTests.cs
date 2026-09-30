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
        var (messages, _) = AiPromptBuilder.Build(ConversationContext.Empty, "what is this?", [], []);

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
        ], WasTrimmed: false, ParentDepth: 2, ConversationId: 100);

        var (messages, _) = AiPromptBuilder.Build(context, "third question", [], []);

        messages.Should().HaveCount(5, "system prompt, three replayed turns, and the new question");
        messages[1].ToPlainText().Should().Contain("<@42>").And.Contain("first question");
        messages[2].Role.Should().Be(AiChatRole.Assistant);
        messages[3].ToPlainText().Should().Contain("<@99>");
    }

    [Fact]
    public void Build_TellsTheModel_WhenHistoryWasTrimmed()
    {
        var context = new ConversationContext([], WasTrimmed: true, ParentDepth: 5, ConversationId: 100);

        var (messages, _) = AiPromptBuilder.Build(context, "question", [], []);

        messages.Should().Contain(m => m.Role == AiChatRole.System && m.ToPlainText().Contains("trimmed"));
    }

    [Fact]
    public void Build_NumbersEveryExcerptAlike_AndSaysWhichComeWithAPicture()
    {
        // One citation style for everything: each extra style a model is taught is one more it gets
        // wrong. Before this, "[image 2]" was written as 【image 2】 and the picture never appeared.
        var (messages, excerpts) = AiPromptBuilder.Build(
            ConversationContext.Empty,
            "area codes?",
            [],
            [
                Hit(KnowledgeChunkKind.Text, "Tunisian codes run 70-79.", country: "tunisia"),
                Hit(KnowledgeChunkKind.Image, "map of area codes", imageUrl: "https://i.imgur.com/map.png")
            ]);

        var prompt = messages[^1].ToPlainText();
        prompt.Should().Contain("[1] Tunisia").And.Contain("Tunisian codes run 70-79.");
        prompt.Should().Contain("[2] (picture)");
        prompt.Should().NotContain("[image");

        excerpts.Should().HaveCount(2);
        excerpts[0].ImageUrl.Should().BeNull();
        excerpts[1].Marker.Should().Be(2, "pictures share the text excerpts' numbering so citations are unambiguous");
        excerpts[1].ImageUrl.Should().Be("https://i.imgur.com/map.png");
    }

    [Fact]
    public void Build_OffersAnImageChunkWithoutItsPicture_AsPlainText()
    {
        // A picture lost during indexing cannot be attached, so announcing one would promise the
        // model an attachment that never appears.
        var (messages, excerpts) = AiPromptBuilder.Build(
            ConversationContext.Empty,
            "area codes?",
            [],
            [Hit(KnowledgeChunkKind.Image, "map of area codes", imageUrl: null)]);

        messages[^1].ToPlainText().Should().NotContain("(picture)").And.NotContain("Pictures you can attach");
        excerpts.Single().ImageUrl.Should().BeNull();
    }

    [Fact]
    public void Build_ListsTheAttachablePictures_NextToTheQuestion()
    {
        // The markers are already in the excerpt labels, but a model that only reads them there tends
        // to treat them as formatting and answer "I can't display images" — the observed failure. The
        // roster restates them at the point of answering as something it can actually do.
        var (messages, _) = AiPromptBuilder.Build(
            ConversationContext.Empty,
            "what does the MR9 look like?",
            [],
            [
                Hit(KnowledgeChunkKind.Text, "The MR9 runs through the centre south."),
                Hit(KnowledgeChunkKind.Image, "wooded hills", imageUrl: "https://relay/a.png"),
                Hit(KnowledgeChunkKind.Image, "dirt tracks", imageUrl: "https://relay/b.png")
            ]);

        var prompt = messages[^1].ToPlainText();
        prompt.Should().Contain("Pictures you can attach: [2], [3]");
        prompt.IndexOf("Pictures you can attach", StringComparison.Ordinal)
            .Should().BeLessThan(prompt.IndexOf("Question:", StringComparison.Ordinal),
                "it has to be the last thing read before the question");
    }

    [Fact]
    public void Build_OmitsThePictureRoster_WhenNothingRetrievedHasAPicture()
    {
        var (messages, _) = AiPromptBuilder.Build(
            ConversationContext.Empty,
            "what does the MR9 look like?",
            [],
            [Hit(KnowledgeChunkKind.Text, "The MR9 runs through the centre south.")]);

        messages[^1].ToPlainText().Should().NotContain("Pictures you can attach");
    }

    [Fact]
    public void Build_StripsOldCitations_FromReplayedAnswers()
    {
        // An earlier answer's "[3]" pointed at that turn's excerpts. The new turn numbers its own from
        // one again, so a replayed "[3]" would read as a citation of whatever is third this time.
        var context = new ConversationContext(
        [
            new ConversationTurnView(AiTurnRole.User, 42, "what are Thai plates like?", []),
            new ConversationTurnView(AiTurnRole.Assistant, 1, "Two letters and four digits [1][3].", [])
        ], WasTrimmed: false, ParentDepth: 1, ConversationId: 100);

        var (messages, _) = AiPromptBuilder.Build(context, "and in the Philippines?", [], []);

        messages[2].Role.Should().Be(AiChatRole.Assistant);
        messages[2].ToPlainText().Should().Be("Two letters and four digits.");
    }

    [Fact]
    public void Build_CarriesImages_WhenOnlyTheReplayedHistoryHasOne()
    {
        // A follow-up to a screenshot attaches nothing itself but still replays the screenshot, so it
        // needs a model that can read it just as much as the original question did.
        var context = new ConversationContext(
        [
            new ConversationTurnView(AiTurnRole.User, 42, "is this Bengali?", ["https://cdn/sign.png"]),
            new ConversationTurnView(AiTurnRole.Assistant, 1, "Yes.", [])
        ], WasTrimmed: false, ParentDepth: 1, ConversationId: 100);

        AiPromptBuilder.Build(context, "how can you tell?", [], []).CarriesImages.Should().BeTrue();
        AiPromptBuilder.Build(ConversationContext.Empty, "how can you tell?", [], []).CarriesImages.Should().BeFalse();
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
    public void SystemPrompt_AnswersWhatBetaTestersReported()
    {
        var prompt = AiPromptBuilder.SystemPrompt;

        prompt.Should().Contain("You cannot see those pictures",
            "told to describe pictures it only knew the text of, the model invented their contents");
        prompt.Should().NotContain("Describe what it shows");
        prompt.Should().Contain("never use any other citation style",
            "every invented style — 【image 1】, [1, 3, 5] — was a picture or a link that went missing");
        prompt.Should().Contain("not necessarily what the user is",
            "a cactus found in a California guide turned into an answer about California");
        prompt.Should().Contain("Answer the whole question",
            "answers stopped at what one guide covered");
    }

    [Fact]
    public void Build_DoesNotRepeatTheCountry_WhenTheSectionPathAlreadyNamesIt()
    {
        // Plonk It section paths are rooted at the country, so prefixing one produces
        // "Eswatini · Eswatini > Spotlight" — which reads as a bug in the citation, not as emphasis.
        var (_, excerpts) = AiPromptBuilder.Build(
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
        var (messages, _) = AiPromptBuilder.Build(ConversationContext.Empty, "question", [], []);

        messages[^1].ToPlainText().Should().Contain("No guide excerpts matched");
    }

    [Fact]
    public void Build_CarriesAttachedImagesOnTheQuestion()
    {
        var (messages, _) = AiPromptBuilder.Build(
            ConversationContext.Empty, "what is this?", ["https://cdn/x.png"], []);

        messages[^1].Parts.OfType<AiImagePart>().Select(p => p.Url).Should().Equal("https://cdn/x.png");
    }

    private static KnowledgeHit Hit(
        KnowledgeChunkKind kind,
        string text,
        string? imageUrl = null,
        string? country = null) =>
        new(Guid.NewGuid(), 0.9f, kind, text, "https://www.plonkit.net/tunisia", imageUrl,
            "Tunisia", country, "Tunisia > Identifying", "Plonk It team", 0);
}
