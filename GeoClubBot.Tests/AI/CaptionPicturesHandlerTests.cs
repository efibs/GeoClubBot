using Configuration;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Ingestion;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// The run that has a vision model describe pictures with no words of their own: what it asks, what it
/// stores, and when it stops. Every request is one of a shared daily allowance, so stopping at the
/// right moment is as much the point as describing.
/// </summary>
public sealed class CaptionPicturesHandlerTests
{
    private const string Model = "vendor/vision:free";

    private readonly IKnowledgeIndex _index = Substitute.For<IKnowledgeIndex>();
    private readonly IChatModelClient _chat = Substitute.For<IChatModelClient>();
    private readonly IChatModelCatalog _catalog = Substitute.For<IChatModelCatalog>();
    private readonly IEmbedder _embedder = Substitute.For<IEmbedder>();
    private readonly IImageRelay _relay = Substitute.For<IImageRelay>();
    private readonly IAiBudgetRepository _budget = Substitute.For<IAiBudgetRepository>();
    private readonly AiCaptionsConfiguration _settings = new() { MaxPicturesPerRun = 10, Version = 2, MaxConsecutiveFailures = 3 };
    private readonly List<PictureDescription> _written = [];
    private int _reservable = int.MaxValue;

    public CaptionPicturesHandlerTests()
    {
        _catalog.ReadChainAsync(Arg.Any<ChatModelRequirements>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>([Model, "vendor/other:free"]));

        _budget.TryReserveRequestsAsync(Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var amount = call.ArgAt<int>(1);
                if (_reservable < amount)
                {
                    return Task.FromResult(false);
                }

                _reservable -= amount;
                return Task.FromResult(true);
            });

        _relay.ReadAsDataUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<string?>($"data:image/png;base64,{call.ArgAt<string>(0).GetHashCode():X}"));

        _embedder.EmbedAsync(Arg.Any<IReadOnlyList<EmbeddingInput>>(), Arg.Any<CancellationToken>())
            .Returns(call => Result<IReadOnlyList<ReadOnlyMemory<float>>>.Success(
                [.. ((IReadOnlyList<EmbeddingInput>)call[0]).Select((_, index) => (ReadOnlyMemory<float>)new float[] { index + 1 })]));

        _index.WriteCaptionsAsync(Arg.Any<IReadOnlyList<PictureDescription>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _written.AddRange((IReadOnlyList<PictureDescription>)call[0]);
                return Task.CompletedTask;
            });

        _index.CountPicturesWantingCaptionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(0L));
    }

    [Fact]
    public async Task DescribesEachPicture_WithAVisionModel_AndStoresTheDescriptionWithItsNewVector()
    {
        var pictures = Pictures("a", "b");
        ArrangeWanting(pictures);
        ArrangeReplies("A gravel road between pines.", "A blue sign with Cyrillic script.");

        var result = await Handler().Handle(new CaptionPicturesCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(new CaptionReport(Attempted: 2, Described: 2, Unusable: 0, Failed: 0, Remaining: 0, BudgetExhausted: false));

        // Only models that read pictures are asked, and each picture travels inline.
        await _catalog.Received().ReadChainAsync(Arg.Is<ChatModelRequirements>(r => r.NeedsImageInput), Arg.Any<CancellationToken>());
        await _chat.Received(2).CompleteAsync(
            Arg.Is<AiChatRequest>(r => r.ModelChain[0] == Model
                                       && r.Messages[r.Messages.Count - 1].Parts.OfType<AiImagePart>().Single().Url.StartsWith("data:image/png")),
            Arg.Any<CancellationToken>());

        _written.Should().HaveCount(2);
        _written.Select(w => w.Id).Should().Equal(pictures[0].Id, pictures[1].Id);
        _written.Select(w => w.Caption).Should().Equal(
            new GeneratedCaption("A gravel road between pines.", 2),
            new GeneratedCaption("A blue sign with Cyrillic script.", 2));
        _written.Should().OnlyContain(w => w.TextVector != null, "the description is embedded with the text");

        // The vector is of the recipe with the description appended, so a question finds the picture by it.
        await _embedder.Received(1).EmbedAsync(
            Arg.Is<IReadOnlyList<EmbeddingInput>>(inputs =>
                inputs.Count == 2 && ((TextEmbeddingInput)inputs[0]).Text.EndsWith("A gravel road between pines.")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClaimsEachRequest_AgainstItsOwnShare_AndStopsWhenItIsSpent()
    {
        ArrangeWanting(Pictures("a", "b", "c"));
        ArrangeReplies("One.", "Two.", "Three.");
        _reservable = 2; // the group's embedding request, then one picture

        var result = await Handler(dailyBudget: 100, percent: 2).Handle(new CaptionPicturesCommand(), CancellationToken.None);

        result.Value.BudgetExhausted.Should().BeTrue();
        result.Value.Attempted.Should().Be(1);
        result.Value.Described.Should().Be(1);
        _written.Should().ContainSingle().Which.TextVector.Should().NotBeNull("the embedding request was claimed before any picture");

        // The ceiling is the share, not the whole allowance.
        await _budget.Received().TryReserveRequestsAsync(Arg.Any<DateOnly>(), 1, 2, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AttemptsNothing_WhenNotEvenTheEmbeddingRequestCanBeAfforded()
    {
        ArrangeWanting(Pictures("a"));
        ArrangeReplies("One.");
        _reservable = 0;

        var result = await Handler().Handle(new CaptionPicturesCommand(), CancellationToken.None);

        result.Value.BudgetExhausted.Should().BeTrue();
        result.Value.Attempted.Should().Be(0);
        await _chat.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default);
    }

    [Fact]
    public async Task StoresWhatItDescribed_WithoutAVector_WhenTheEmbedderFails()
    {
        ArrangeWanting(Pictures("a"));
        ArrangeReplies("One.");
        _embedder.EmbedAsync(Arg.Any<IReadOnlyList<EmbeddingInput>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<ReadOnlyMemory<float>>>.Failure(Error.Unexpected("ai.embedding_failed", "502")));

        var result = await Handler().Handle(new CaptionPicturesCommand(), CancellationToken.None);

        result.Value.Described.Should().Be(1);
        _written.Should().ContainSingle().Which.TextVector.Should().BeNull();
        _written[0].Caption.Text.Should().Be("One.", "a description paid for is never thrown away");
    }

    [Fact]
    public async Task GivesBackTheEmbeddingRequest_WhenAGroupDescribedNothing()
    {
        ArrangeWanting(Pictures("a"));
        ArrangeReplies("I cannot see the image.");

        await Handler().Handle(new CaptionPicturesCommand(), CancellationToken.None);

        await _budget.Received(1).ReleaseRequestsAsync(Arg.Any<DateOnly>(), 1, Arg.Any<CancellationToken>());
        await _embedder.DidNotReceiveWithAnyArgs().EmbedAsync(default!, default);
    }

    [Fact]
    public async Task LeavesAPictureForALaterRun_WhenTheReplyIsNoDescription_AndRecordsTheModelsUsage()
    {
        ArrangeWanting(Pictures("a", "b"));
        ArrangeReplies("I'm sorry, I can't identify locations.", "A red soil track.");

        var result = await Handler().Handle(new CaptionPicturesCommand(), CancellationToken.None);

        result.Value.Should().BeEquivalentTo(new CaptionReport(2, Described: 1, Unusable: 1, Failed: 0, Remaining: 0, BudgetExhausted: false));
        _written.Should().ContainSingle().Which.Caption.Text.Should().Be("A red soil track.");

        // Billed and credited whatever was made of the reply.
        await _budget.Received(2).RecordTokenUsageAsync(Arg.Any<DateOnly>(), 10, 5, Arg.Any<CancellationToken>());
        _catalog.Received(2).ReportChainOutcome(Arg.Any<IReadOnlyList<string>>(), Model);
    }

    [Fact]
    public async Task StopsAfterUnusableRepliesInARow_LeavingTheRestForANight_WhenTheModelDescribes()
    {
        ArrangeWanting(Pictures("a", "b", "c", "d", "e"));
        ArrangeReplies("", "safe", "I cannot see the image.", "A road.", "A sign.");

        var result = await Handler().Handle(new CaptionPicturesCommand(), CancellationToken.None);

        result.Value.StoppedForFailures.Should().BeTrue();
        result.Value.Attempted.Should().Be(3);
        result.Value.Unusable.Should().Be(3);
        _written.Should().BeEmpty();
    }

    [Fact]
    public async Task DemotesTheModel_AndAsksTheNextChain_WhenARequestFails()
    {
        ArrangeWanting(Pictures("a", "b"));
        _chat.CompleteAsync(Arg.Any<AiChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result<AiChatResponse>.Failure(Error.Unexpected(ChatErrorCodes.RequestFailed, "upstream 502")),
                Reply("A road."));

        var result = await Handler().Handle(new CaptionPicturesCommand(), CancellationToken.None);

        result.Value.Failed.Should().Be(1);
        result.Value.Described.Should().Be(1);
        _catalog.Received(1).ReportChainOutcome(Arg.Any<IReadOnlyList<string>>(), null);
        await _catalog.Received(2).ReadChainAsync(Arg.Any<ChatModelRequirements>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopsTheRun_WhenTheProviderIsStillRateLimiting()
    {
        ArrangeWanting(Pictures("a", "b"));
        _chat.CompleteAsync(Arg.Any<AiChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<AiChatResponse>.Failure(Error.Unexpected(ChatErrorCodes.RateLimited, "429")));

        var result = await Handler().Handle(new CaptionPicturesCommand(), CancellationToken.None);

        result.Value.RateLimited.Should().BeTrue();
        result.Value.Attempted.Should().Be(1, "every further picture would meet the same wall");
    }

    [Fact]
    public async Task AttemptsNothing_WhenNoFreeModelReadsPictures()
    {
        ArrangeWanting(Pictures("a"));
        _catalog.ReadChainAsync(Arg.Any<ChatModelRequirements>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>([]));
        _index.CountPicturesWantingCaptionAsync(2, Arg.Any<CancellationToken>()).Returns(Task.FromResult(1L));

        var result = await Handler().Handle(new CaptionPicturesCommand(), CancellationToken.None);

        result.Value.NoModel.Should().BeTrue();
        result.Value.Attempted.Should().Be(0);
        result.Value.Remaining.Should().Be(1);
        await _budget.DidNotReceiveWithAnyArgs().TryReserveRequestsAsync(default, default, default, default);
    }

    [Fact]
    public async Task ReadsOnlyThePicturesOfTheCurrentVersion_UpToTheRunsLimit()
    {
        ArrangeWanting([]);

        await Handler().Handle(new CaptionPicturesCommand(MaxPictures: 7), CancellationToken.None);

        await _index.Received().ReadPicturesWantingCaptionAsync(2, 7, Arg.Any<CancellationToken>());
        await _chat.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default);
    }

    [Fact]
    public async Task RefusesToRun_WhenTurnedOff()
    {
        _settings.Enabled = false;

        var result = await Handler().Handle(new CaptionPicturesCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(CaptionPicturesHandler.DisabledCode);
        await _index.DidNotReceiveWithAnyArgs().ReadPicturesWantingCaptionAsync(default, default, default);
    }

    [Fact]
    public async Task SendsThePictureByItsLink_WhenTheRelayHoldsNoCopy()
    {
        ArrangeWanting(Pictures("a"));
        ArrangeReplies("A road.");
        _relay.ReadAsDataUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(null));

        await Handler().Handle(new CaptionPicturesCommand(), CancellationToken.None);

        await _chat.Received().CompleteAsync(
            Arg.Is<AiChatRequest>(r => r.Messages[r.Messages.Count - 1].Parts.OfType<AiImagePart>().Single().Url == "https://relay/a.png"),
            Arg.Any<CancellationToken>());
    }

    private CaptionPicturesHandler Handler(int dailyBudget = 100, int percent = 20)
    {
        _settings.MaxDailyBudgetPercent = percent;
        var ai = new AiConfiguration { OpenRouter = { DailyRequestBudget = dailyBudget, EmbeddingBatchSize = 32 } };

        return new CaptionPicturesHandler(
            _index, _chat, _catalog, _embedder, _relay, _budget,
            Options.Create(ai), Options.Create(_settings),
            new FakeTimeProvider(new DateTimeOffset(2026, 11, 12, 4, 30, 0, TimeSpan.Zero)),
            NullLogger<CaptionPicturesHandler>.Instance);
    }

    private void ArrangeWanting(IReadOnlyList<KnowledgeHit> pictures) =>
        _index.ReadPicturesWantingCaptionAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(pictures));

    private void ArrangeReplies(params string[] replies) =>
        _chat.CompleteAsync(Arg.Any<AiChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(Reply(replies[0]), [.. replies.Skip(1).Select(Reply)]);

    private static Result<AiChatResponse> Reply(string text) =>
        Result<AiChatResponse>.Success(new AiChatResponse(text, Model, new AiTokenUsage(10, 5)));

    private static IReadOnlyList<KnowledgeHit> Pictures(params string[] keys) =>
        [.. keys.Select(key => new KnowledgeHit(
            Guid.NewGuid(), 0f, KnowledgeChunkKind.Image, "Guide", "https://docs.google.com/document/d/x", $"https://relay/{key}.png",
            "Guide", "colombia", "Guide > Vegetation", "Author", 0, "gdoc", "x", key))];
}
