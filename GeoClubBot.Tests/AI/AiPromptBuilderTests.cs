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
        prompt.Should().Contain("[2] (picture");
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
        var prompt = Normalise(AiPromptBuilder.SystemPrompt);

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

    [Theory]
    [InlineData("Lead with the direct answer to the exact question",
        "\"a clear answer 'They are by far most common on the mainland but can be found elsewhere too' would have been great\"")]
    [InlineData("never become \"only\" or \"never\"", "\"99% Peninsula\" was answered as \"mainly\"")]
    [InlineData("Never turn a vague note into a complete list", "\"notably rare in the Northeast\" became nine named states")]
    [InlineData("say what you infer", "inferences were stated as the guides' facts")]
    [InlineData("related but different from what was asked", "\"I asked about snake traps. Not possum guards\"")]
    [InlineData("stays in that country", "\"I don't care about other countries if I ask about regionguessing Australia\"")]
    [InlineData("judge the screenshot against that country's clues", "the bollard \"wasn't Victoria\"")]
    [InlineData("A follow-up continues the earlier turns' topic and country", "\"How can I clearly identify Sabah?\" was answered for Malaysia as a whole")]
    [InlineData("Licence plates and faces are blurred", "\"the license plate thingy is basically useless in GeoGuessr\"")]
    [InlineData("cite a picture that shows the distribution", "\"without the image ... that shows the exact distribution, this answer is basically useless\"")]
    [InlineData("never say a picture is attached unless you cite it", "\"shown in the attached pictures\" was written of pictures never attached")]
    public void SystemPrompt_AnswersWhatTheRatedAnswersGotWrong(string instruction, string because) =>
        Normalise(AiPromptBuilder.SystemPrompt).Should().Contain(Normalise(instruction), because);

    [Fact]
    public void SystemPrompt_StaysShortEnoughForAFreeModelToFollow()
    {
        // One short instruction per lesson: free models follow a short list and lose a long one.
        AiPromptBuilder.SystemPrompt.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Should().HaveCountLessThan(550);
    }

    /// <summary>The prompt as a model reads it: one line, whatever the source's wrapping.</summary>
    private static string Normalise(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

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

    [Fact]
    public void AppendToSystemPrompt_AddsTheToolsParagraph_ToTheOneSystemPrompt()
    {
        var prompt = AiPromptBuilder.Build(ConversationContext.Empty, "question", [], [Hit(KnowledgeChunkKind.Text, "Poles.")]);

        var offered = AiPromptBuilder.AppendToSystemPrompt(prompt, "You can also call tools.");

        offered.Messages.Should().HaveCount(prompt.Messages.Count, "a model that weighs only the first system message still reads it");
        offered.Messages[0].ToPlainText().Should().Be($"{AiPromptBuilder.SystemPrompt}\n\nYou can also call tools.");
        offered.Messages.Skip(1).Should().Equal(prompt.Messages.Skip(1));
        offered.Excerpts.Should().BeSameAs(prompt.Excerpts);
    }

    [Fact]
    public void FormatExcerpt_ReadsExactlyAsTheExcerptsTheQuestionComesWith()
    {
        // The tools' results are numbered excerpts too, and must read the same however they were found.
        var picture = Hit(KnowledgeChunkKind.Image, "The bollard is white.", imageUrl: "https://relay/b.png", country: "tunisia");
        var (messages, _) = AiPromptBuilder.Build(ConversationContext.Empty, "question", [], [picture]);

        var formatted = AiPromptBuilder.FormatExcerpt(picture, 1, out var excerpt);

        messages[^1].ToPlainText().Should().Contain(formatted);
        formatted.Should().StartWith("[1] (picture, usually with a distribution map) Tunisia > Identifying");
        excerpt.Should().BeEquivalentTo(new OfferedExcerpt(1, "https://www.plonkit.net/tunisia", "Tunisia > Identifying", "https://relay/b.png", "Tunisia")
        {
            ChunkIds = [picture.Id]
        });
    }

    [Theory]
    [InlineData("plonkit", "https://www.plonkit.net/brazil#PvkL", "[1] (picture, usually with a distribution map) ")]
    [InlineData(null, "https://www.plonkit.net/brazil#PvkL", "[1] (picture, usually with a distribution map) ")]
    [InlineData("imgur", "https://imgur.com/a/ZfShHTQ", "[1] (picture) ")]
    [InlineData("gslides", "https://docs.google.com/presentation/d/x/edit#slide=id.p8", "[1] (picture) ")]
    public void FormatExcerpt_MarksAPlonkItPicture_AsUsuallyCarryingADistributionMap(string? sourceType, string sourceUrl, string expected)
    {
        // About 70 % of PlonkIt tip pictures carry a small distribution map, yet their text seldom says
        // so: shown only "(picture)", the model told a user asking for the distribution "I don't have
        // the actual distribution image" while it cited one.
        var picture = Hit(KnowledgeChunkKind.Image, "Asymmetrical crossbar with no supports.", imageUrl: "https://relay/p.png")
            with
        { SourceUrl = sourceUrl, SourceType = sourceType };

        AiPromptBuilder.FormatExcerpt(picture, 1, out _).Should().StartWith(expected);
    }

    [Fact]
    public void FormatExcerpt_NeverMarksPlonkItText_AsAPicture()
    {
        var text = Hit(KnowledgeChunkKind.Text, "Pole supports are rare in the Northeast.") with { SourceType = "plonkit" };

        AiPromptBuilder.FormatExcerpt(text, 1, out _).Should().NotContain("(picture").And.NotContain("distribution map");
    }

    [Fact]
    public void FormatExcerpt_CountsThePicturesOfTheCopiesMergedIntoIt()
    {
        // One slide caption beside three of its pictures is one excerpt, and citing it attaches all three.
        var firstCopy = Guid.NewGuid();
        var secondCopy = Guid.NewGuid();
        var slide = Hit(KnowledgeChunkKind.Image, "Brazilian Poles Lite > Slide 7", imageUrl: "https://relay/1.png") with
        {
            SourceUrl = "https://docs.google.com/presentation/d/x/edit#slide=id.p7",
            ExtraImageUrls = ["https://relay/2.png", "https://relay/1.png", "https://relay/3.png"],
            MergedChunkIds = [firstCopy, secondCopy]
        };

        var formatted = AiPromptBuilder.FormatExcerpt(slide, 4, out var excerpt);

        formatted.Should().StartWith("[4] (3 pictures) ");
        excerpt.ImageUrls.Should().Equal("https://relay/1.png", "https://relay/2.png", "https://relay/3.png");
        excerpt.ChunkIds.Should().Equal(slide.Id, firstCopy, secondCopy);
    }

    [Fact]
    public void FormatExcerpt_OffersNoPictures_ForTextWhoseCopiesHadSome()
    {
        // Only a picture chunk is announced as one; a text excerpt never promises an attachment.
        var text = Hit(KnowledgeChunkKind.Text, "Possum guards are common in Kalimantan.") with { ExtraImageUrls = ["https://relay/x.png"] };

        AiPromptBuilder.FormatExcerpt(text, 1, out var excerpt).Should().NotContain("picture");
        excerpt.ImageUrls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1, 3, "([1] is the excerpt your previous answer cited; the rest were found for this question.)")]
    [InlineData(2, 3, "([1] to [2] are the excerpts your previous answer cited, in the order it cited them; the rest were found for this question.)")]
    [InlineData(3, 3, "([1] to [3] are the excerpts your previous answer cited, in the order it cited them.)")]
    [InlineData(5, 3, "([1] to [3] are the excerpts your previous answer cited, in the order it cited them.)")]
    public void Build_SaysWhichExcerptsThePreviousAnswerCited(int carried, int total, string expected)
    {
        // "Can you show me an image of the distribution?" meant the picture the answer before had cited
        // as [1]; nothing else in the prompt tells the model which excerpt that was.
        var hits = Enumerable.Range(1, total).Select(index => Hit(KnowledgeChunkKind.Text, $"Excerpt {index}.")).ToList();

        var (messages, _) = AiPromptBuilder.Build(ConversationContext.Empty, "show me that", [], hits, carried);

        var prompt = messages[^1].ToPlainText();
        prompt.Should().Contain(expected);
        prompt.IndexOf(expected, StringComparison.Ordinal).Should()
            .BeGreaterThan(prompt.IndexOf("Guide excerpts:", StringComparison.Ordinal))
            .And.BeLessThan(prompt.IndexOf("Excerpt 1.", StringComparison.Ordinal), "it introduces the excerpts");
    }

    [Fact]
    public void Build_SaysNothingOfAPreviousAnswer_WhenNothingWasCarried()
    {
        var (messages, _) = AiPromptBuilder.Build(
            ConversationContext.Empty, "question", [], [Hit(KnowledgeChunkKind.Text, "Poles.")], carried: 0);

        messages[^1].ToPlainText().Should().NotContain("previous answer");
    }

    private static KnowledgeHit Hit(
        KnowledgeChunkKind kind,
        string text,
        string? imageUrl = null,
        string? country = null) =>
        new(Guid.NewGuid(), 0.9f, kind, text, "https://www.plonkit.net/tunisia", imageUrl,
            "Tunisia", country, "Tunisia > Identifying", "Plonk It team", 0);
}
