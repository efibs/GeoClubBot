using Configuration;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Review;
using UseCases.UseCases.AI.StreetView;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// The vision check of #480: which views show what the player saw, what the model is asked, how its
/// reply is read, and how what each round showed becomes the excerpts a review offers.
/// </summary>
public sealed class ClueSightTests
{
    [Fact]
    public void Prompt_ListsTheGuidesCluesForThePlace_AndAsksForAVerdictOnEach()
    {
        var messages = ClueSightPrompt.BuildMessages(
            ["data:image/jpeg;base64,AA==", "data:image/jpeg;base64,AB=="],
            [new StreetViewAngle(73, 0, 90), new StreetViewAngle(163, 0, 90)],
            "Spain",
            "Valencian Community",
            "Portugal",
            [Hit("Guardrails", "Yellow reflectors on guardrails are common in Spain."), Hit("Bollards", "White bollards with a black band.")]);

        messages[0].ToPlainText().Should().Contain("Be strict. A clue counts only when you can point at it in a picture");
        var user = messages[1];
        user.Parts.OfType<AiImagePart>().Select(part => part.Url).Should().Equal("data:image/jpeg;base64,AA==", "data:image/jpeg;base64,AB==");
        user.ToPlainText().Should().StartWith(
            "The 2 pictures are views of one Street View place in Spain (Valencian Community). Looking picture 1 east, picture 2 south.\nThe player guessed Portugal.");
        user.ToPlainText().Should().Contain("1. Guide > Guardrails: Yellow reflectors on guardrails are common in Spain.\n2. Guide > Bollards: White bollards with a black band.");
        user.ToPlainText().Should().EndWith("Last, one line: \"Best picture: n\", the picture that shows the most telling clue.");
    }

    [Fact]
    public void Parse_ReadsTheVerdicts_TheNotes_AndTheBestPicture()
    {
        const string reply = """
            **1: Yes** — picture 2, right side: yellow reflectors on the guardrail
            2: no
            3. unclear — picture 1, too far away
            [4] Yes, picture 3: a white bollard
            5: not visible
            1: no
            9: yes — out of range
            Seen: red soil (picture 1)
            Seen: 3 Yellow dashed centre line
            - Seen: none
            Best picture: 2
            """;

        var verdict = ClueSightPrompt.Parse(reply, candidateCount: 5, pictureCount: 4);

        verdict.Seen.Should().Equal(
            new SeenClue(0, "picture 2, right side: yellow reflectors on the guardrail"),
            new SeenClue(3, "picture 3: a white bollard"));
        verdict.Judged.Should().Be(5, "a repeated number keeps its first verdict, and one out of range is ignored");
        verdict.AlsoSeen.Should().Equal("red soil (picture 1)", "picture 3: Yellow dashed centre line");
        verdict.BestPicture.Should().Be(1);
        verdict.IsUsable.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("I'm sorry, I can't help with that.")]
    public void Parse_FindsNothingUsable_InAReplyThatIsNoChecklist(string? reply) =>
        ClueSightPrompt.Parse(reply, 3, 4).IsUsable.Should().BeFalse();

    [Fact]
    public void Angles_ShowTheFixedView_OrTheWholePanoramaInQuarters()
    {
        StreetViewAngles.ForRound(PanoramaViewMode.FixedView, 225, -33.5, 0).Should().Equal(new StreetViewAngle(225, -33.5, 126.87));
        StreetViewAngles.ForRound(PanoramaViewMode.FixedView, 10, 0, 1)[0].FieldOfView.Should().BeApproximately(90, 0.1, "GeoGuessr's zoom 1 is about a quarter-turn");
        StreetViewAngles.ForRound(PanoramaViewMode.FullPanorama, 300, 20, 2).Select(angle => angle.Heading).Should().Equal(300, 30, 120, 210);
        StreetViewAngles.ForRound(PanoramaViewMode.FullPanorama, 300, 20, 2).Should().OnlyContain(angle => angle.Pitch == 0 && angle.FieldOfView == 90);

        var spot = StreetViewAngles.ForSpot(new StreetViewSpot("p", 1, 2, 51.57, 2.16, 75));
        spot.Should().HaveCount(4);
        spot[0].Should().Be(new StreetViewAngle(51.57, 2.16, 75), "the link's own view comes first, as it showed it");
    }

    [Fact]
    public void Excerpts_OfferWhatEachRoundShowed_ThenWhatToLookFor_TakingTurns()
    {
        var spainCountry = Clue("Guardrails", RoundSearchKind.Country);
        var spainRoads = Clue("Road lines", RoundSearchKind.Country);
        var spainRegion = Clue("Valencia", RoundSearchKind.Region);
        var thaiCountry = Clue("Thai poles", RoundSearchKind.Country);
        var thaiRegion = Clue("Isan", RoundSearchKind.Region);
        var ghanaCountry = Clue("Ghana poles", RoundSearchKind.Country);

        var rounds = new[]
        {
            new ReviewedRound(1, "ES", "Valencia", "PT", null, 300, 3000),
            new ReviewedRound(2, "TH", "Isan", "TH", null, 200, 3500),
            new ReviewedRound(3, "GH", null, "NG", null, 900, 1000),
            new ReviewedRound(4, "TH", null, "TH", null, 1, 4999)
        };
        IReadOnlyList<IReadOnlyList<RoundClue>> clues =
        [
            [spainCountry, spainRoads, spainRegion],
            [thaiCountry, thaiRegion],
            [ghanaCountry],
            [thaiCountry]
        ];
        IReadOnlyList<PlaceSight?> sights =
        [
            Sight(new SeenClue(2, "picture 1: palms"), new SeenClue(0, "picture 2: yellow reflectors")),
            Sight(),
            null,
            Sight()
        ];

        var (hits, reviewed) = ReviewExcerpts.Assemble(rounds, clues, sights, wellPlayedPoints: 4500, unseenForMissed: 2, maxExcerpts: 30);

        // Round by round, in turns: Spain's seen guardrails, Thailand's region to look for, Ghana's country
        // to look for (nothing is known of it), then Spain's seen region.
        hits.Should().Equal(spainCountry.Hit, thaiRegion.Hit, ghanaCountry.Hit, spainRegion.Hit);
        reviewed[0].Evidence!.Seen.Should().Equal(new SeenExcerpt(1, "picture 2: yellow reflectors", false), new SeenExcerpt(4, "picture 1: palms", true));
        reviewed[0].Evidence!.ToLookFor.Should().BeEmpty("a country-wide clue was seen in a missed round");
        reviewed[1].Evidence!.Should().BeEquivalentTo(new { Checked = true, Seen = Array.Empty<SeenExcerpt>(), ToLookFor = new[] { 2 } });
        reviewed[2].Evidence!.Should().BeEquivalentTo(new { Checked = false, ToLookFor = new[] { 3 } });
        reviewed[3].Evidence!.Should().BeEquivalentTo(new { Checked = true, Seen = Array.Empty<SeenExcerpt>(), ToLookFor = Array.Empty<int>() }, "a well-played round needs nothing to look for");
    }

    [Fact]
    public void Excerpts_OfferTheCountrysBest_ForAMissedRoundThatShowedNone_AndStopAtTheLimit()
    {
        var clues = new[] { Clue("A", RoundSearchKind.Country), Clue("B", RoundSearchKind.Contrast), Clue("C", RoundSearchKind.Country), Clue("R", RoundSearchKind.Region) };

        var (hits, reviewed) = ReviewExcerpts.Assemble(
            [new ReviewedRound(1, "ES", "Valencia", "PT", null, 300, 3000)], [clues], [Sight()], 4500, unseenForMissed: 2, maxExcerpts: 1);

        hits.Should().Equal(clues[0].Hit);
        reviewed[0].Evidence!.ToLookFor.Should().Equal(1);
    }

    [Fact]
    public async Task Check_ClaimsEveryPlacesRequestUpFront_LooksSideBySide_AndGivesBackWhatItDidNotSpend()
    {
        var harness = new CheckHarness();
        harness.Chat.CompleteAsync(Arg.Any<AiChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<AiChatResponse>.Success(new AiChatResponse("1: yes — picture 1, a bollard\nBest picture: 1", "vision/model", new AiTokenUsage(1000, 20))));

        var sights = await harness.Check().LookAsync([harness.Request("a"), harness.Request("b"), harness.Request("missing")], new DateOnly(2026, 11, 21), CancellationToken.None);

        sights[0]!.Checked.Should().BeTrue();
        sights[0]!.Verdict!.Seen.Should().ContainSingle();
        sights[0]!.Model.Should().Be("vision/model");
        sights[1]!.Checked.Should().BeTrue();
        sights[2].Should().BeNull("Google served no view of it");
        await harness.Budget.Received(1).TryReserveRequestsAsync(new DateOnly(2026, 11, 21), 4, 100, Arg.Any<CancellationToken>());
        await harness.Budget.Received(1).ReleaseRequestsAsync(new DateOnly(2026, 11, 21), 2, Arg.Any<CancellationToken>());
        await harness.Budget.Received(1).RecordTokenUsageAsync(new DateOnly(2026, 11, 21), 2000, 40, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Check_RetriesOnceAgainstOtherModels_WhenTheFirstReplyIsNoChecklist()
    {
        var harness = new CheckHarness();
        harness.Catalog.ReadChainAsync(Arg.Any<ChatModelRequirements>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["first/model", "second/model"]), Task.FromResult<IReadOnlyList<string>>(["first/model", "second/model", "third/model"]));
        harness.Chat.CompleteAsync(Arg.Any<AiChatRequest>(), Arg.Any<CancellationToken>()).Returns(
            Result<AiChatResponse>.Success(new AiChatResponse("A lovely road.", "first/model", new AiTokenUsage(1, 1))),
            Result<AiChatResponse>.Success(new AiChatResponse("1: no\nSeen: a eucalyptus (picture 2)", "third/model", new AiTokenUsage(1, 1))));

        var sight = (await harness.Check().LookAsync([harness.Request("a")], new DateOnly(2026, 11, 21), CancellationToken.None)).Single()!;

        sight.Checked.Should().BeTrue();
        sight.Model.Should().Be("third/model");
        sight.Verdict!.AlsoSeen.Should().Equal("a eucalyptus (picture 2)");
        await harness.Chat.Received(1).CompleteAsync(Arg.Is<AiChatRequest>(request => request.ModelChain.SequenceEqual(new[] { "third/model" })), Arg.Any<CancellationToken>());
        await harness.Budget.DidNotReceiveWithAnyArgs().ReleaseRequestsAsync(default, default, default);
    }

    [Fact]
    public async Task Check_LeavesThePlacesUnchecked_WhenTheAllowanceCannotPay()
    {
        var harness = new CheckHarness();
        harness.Budget.TryReserveRequestsAsync(Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));

        var sight = (await harness.Check().LookAsync([harness.Request("a")], new DateOnly(2026, 11, 21), CancellationToken.None)).Single()!;

        sight.Checked.Should().BeFalse();
        sight.Pictures.Should().HaveCount(4, "the views are still there to show");
        await harness.Chat.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default);
    }

    private static PlaceSight Sight(params SeenClue[] seen) =>
        new([new StreetViewPicture([1], "image/jpeg", new StreetViewAngle(0, 0, 90))], new ClueSightVerdict(seen, [], 0, Math.Max(1, seen.Length)), "vision/model");

    private static RoundClue Clue(string heading, RoundSearchKind kind) => new(Hit(heading, $"{heading} text."), kind);

    private static KnowledgeHit Hit(string heading, string text) =>
        new(Guid.NewGuid(), 0.5f, KnowledgeChunkKind.Text, text, $"https://example.org/{heading}", null, "Guide", "spain", $"Guide > {heading}", null, 0);

    private sealed class CheckHarness
    {
        public IStreetViewImagery Imagery { get; } = Substitute.For<IStreetViewImagery>();
        public IChatModelClient Chat { get; } = Substitute.For<IChatModelClient>();
        public IChatModelCatalog Catalog { get; } = Substitute.For<IChatModelCatalog>();
        public IAiBudgetRepository Budget { get; } = Substitute.For<IAiBudgetRepository>();

        public CheckHarness()
        {
            Imagery.ReadViewsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<StreetViewAngle>>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult<IReadOnlyList<StreetViewPicture>>((string)call[0] == "missing"
                    ? []
                    : [.. ((IReadOnlyList<StreetViewAngle>)call[1]).Select(angle => new StreetViewPicture([1, 2], "image/jpeg", angle))]));
            Catalog.ReadChainAsync(Arg.Any<ChatModelRequirements>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<string>>(["vision/model"]));
            Budget.TryReserveRequestsAsync(Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));
        }

        public SightRequest Request(string panoId) =>
            new(panoId, StreetViewAngles.ForRound(PanoramaViewMode.FullPanorama, 0, 0, 0), "Spain", null, null, [Hit("Bollards", "White bollards.")]);

        public ClueSightCheck Check() =>
            new(Imagery, Chat, Catalog, Budget,
                Options.Create(new AiConfiguration { Active = true, OpenRouter = { DailyRequestBudget = 100 } }),
                Options.Create(new AiRoundReviewConfiguration()),
                NullLogger<ClueSightCheck>.Instance);
    }
}
