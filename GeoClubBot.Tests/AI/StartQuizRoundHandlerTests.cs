using Configuration;
using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Quiz;
using UseCases.UseCases.AI.Retrieval;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// Which question a quiz asks (#491): a rated picture that makes the question wanted; "where in" or
/// "what is typical" when the member named the country; a few pictures rated on the spot when none fits.
/// </summary>
public sealed class StartQuizRoundHandlerTests
{
    private const string Model = "vendor/vision:free";

    private const string Rating = """
        Country shown: no
        Region shown: no
        Country: easy
        Country wrong: Latvia; Lithuania; Finland
        Region: no
        Clue: no
        """;

    private readonly IKnowledgeIndex _index = Substitute.For<IKnowledgeIndex>();
    private readonly IAiQuizItemRepository _items = Substitute.For<IAiQuizItemRepository>();
    private readonly IAiQuizRoundRepository _rounds = Substitute.For<IAiQuizRoundRepository>();
    private readonly IChatModelClient _chat = Substitute.For<IChatModelClient>();
    private readonly IChatModelCatalog _catalog = Substitute.For<IChatModelCatalog>();
    private readonly IImageRelay _relay = Substitute.For<IImageRelay>();
    private readonly IAiBudgetRepository _budget = Substitute.For<IAiBudgetRepository>();
    private readonly AiQuizConfiguration _settings = new() { Draws = 10, OnTheSpotRatings = 2 };
    private readonly AiConfiguration _ai = new() { Active = true };
    private readonly List<KnowledgeHit> _chunks = [];

    public StartQuizRoundHandlerTests()
    {
        _index.ListCountriesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["estonia", "mongolia", "usa", "united states", "sweden", "norway", "denmark"]));
        _index.SampleAsync(Arg.Any<KnowledgeSample>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<KnowledgeHit>>([]));
        _index.ReadChunksAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<KnowledgeHit>>(
                [.. _chunks.Where(chunk => call.ArgAt<IReadOnlyList<Guid>>(0).Contains(chunk.Id))]));

        _items.DrawAsync(Arg.Any<QuizItemDraw>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AiQuizItem>>([]));
        _items.ReadContentHashesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>()));

        _catalog.ReadChainAsync(Arg.Any<ChatModelRequirements>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>([Model]));
        _budget.TryReserveRequestsAsync(Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        _relay.ReadAsDataUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>("data:image/png;base64,AAAA"));
        _chat.CompleteAsync(Arg.Any<AiChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<AiChatResponse>.Success(new AiChatResponse(Rating, Model, new AiTokenUsage(100, 50)))));
    }

    [Fact]
    public async Task WithoutACountry_AsksWhichCountry_FromARatedPicture()
    {
        Rated(AiQuizQuestionKind.Country, "Estonia", AiQuizDifficulty.Easy);

        var result = await Handler().Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        var round = result.Value;
        round.Kind.Should().Be(AiQuizQuestionKind.Country);
        round.CorrectOption.Should().Be("Estonia");
        round.Options.Should().BeEquivalentTo("Estonia", "Decoy 1", "Decoy 2", "Decoy 3");
        round.CountryName.Should().BeNull("naming the country would answer the question");
        round.Explanation.Should().StartWith("Estonian bollards");
        round.RevealAtUtc.Should().Be(round.AskedAtUtc.AddMinutes(10));
        _rounds.Received(1).Add(round);

        await _items.Received(1).DrawAsync(
            Arg.Is<QuizItemDraw>(draw => draw.Kind == AiQuizQuestionKind.Country && draw.Countries == null
                                         && draw.Difficulties.SequenceEqual(new[] { AiQuizDifficulty.Easy, AiQuizDifficulty.Medium })),
            Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithACountry_AsksWhereInIt()
    {
        Rated(AiQuizQuestionKind.Region, "Khovd", AiQuizDifficulty.Medium, "mongolia");
        Rated(AiQuizQuestionKind.Clue, "The roof rack", AiQuizDifficulty.Medium, "mongolia");

        var result = await Handler().Handle(Command("Mongolia"), CancellationToken.None);

        result.Value.Kind.Should().Be(AiQuizQuestionKind.Region);
        result.Value.CountryName.Should().Be("Mongolia");
        result.Value.CorrectOption.Should().Be("Khovd");
        await _items.DidNotReceive().DrawAsync(Arg.Is<QuizItemDraw>(draw => draw.Kind == AiQuizQuestionKind.Country), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithACountry_AsksWhatIsTypical_WhenNoRegionalClueIsRated()
    {
        Rated(AiQuizQuestionKind.Clue, "The roof rack", AiQuizDifficulty.Medium, "mongolia");

        var result = await Handler().Handle(Command("Mongolia"), CancellationToken.None);

        result.Value.Kind.Should().Be(AiQuizQuestionKind.Clue);
        result.Value.CorrectOption.Should().Be("The roof rack");
    }

    [Fact]
    public async Task DrawsOnlyTheDifficultyAsked()
    {
        Rated(AiQuizQuestionKind.Country, "Estonia", AiQuizDifficulty.Hard);

        await Handler().Handle(Command(difficulty: AiQuizDifficulty.Hard), CancellationToken.None);

        await _items.Received().DrawAsync(
            Arg.Is<QuizItemDraw>(draw => draw.Difficulties.SequenceEqual(new[] { AiQuizDifficulty.Hard })),
            Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForgetsARatingWhosePictureChanged_AndAsksTheNext()
    {
        var changed = QuizRounds.Picture("Estonian bollards carry a red reflector on a black band, unlike Latvian ones.");
        var kept = QuizRounds.Picture("Estonian bollards have a red reflector.");
        _chunks.Add(changed with { Text = "Since re-indexed with other words." });
        _chunks.Add(kept);
        _items.DrawAsync(Arg.Is<QuizItemDraw>(draw => draw.Kind == AiQuizQuestionKind.Country), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AiQuizItem>>([Item(changed, AiQuizQuestionKind.Country, "Estonia"), Item(kept, AiQuizQuestionKind.Country, "Estonia")]));

        var result = await Handler().Handle(Command(), CancellationToken.None);

        result.Value.ChunkId.Should().Be(kept.Id);
        await _items.Received(1).RemoveAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { changed.Id })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RatesAPictureOnTheSpot_WhenNoRatedOneFits_FromTheWholeAllowance()
    {
        var picture = QuizRounds.Picture("Estonian bollards carry a red reflector on a black band, unlike Latvian ones.");
        _index.SampleAsync(Arg.Any<KnowledgeSample>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<KnowledgeHit>>([picture]));

        var result = await Handler().Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        result.Value.CorrectOption.Should().Be("Estonia");
        result.Value.Options.Should().BeEquivalentTo("Estonia", "Latvia", "Lithuania", "Finland");
        await _items.Received(1).ReplaceAsync(Arg.Is<AiQuizItem>(item => item.ChunkId == picture.Id && item.ModelId == Model), Arg.Any<CancellationToken>());
        await _budget.Received(1).TryReserveRequestsAsync(Arg.Any<DateOnly>(), 1, _ai.OpenRouter.DailyRequestBudget, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaysItIsNotReady_WhenTheAllowanceIsSpent()
    {
        _index.SampleAsync(Arg.Any<KnowledgeSample>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<KnowledgeHit>>([QuizRounds.Picture("Estonian bollards carry a red reflector on a black band, unlike Latvian ones.")]));
        _budget.TryReserveRequestsAsync(Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var result = await Handler().Handle(Command(), CancellationToken.None);

        result.Error.Code.Should().Be(QuizErrorCodes.NotReady);
        await _chat.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default);
        _rounds.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task RatesNothingOnTheSpot_WhenTurnedOff()
    {
        _settings.OnTheSpotRatings = 0;
        _index.SampleAsync(Arg.Any<KnowledgeSample>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<KnowledgeHit>>([QuizRounds.Picture("Estonian bollards carry a red reflector on a black band, unlike Latvian ones.")]));

        (await Handler().Handle(Command(), CancellationToken.None)).Error.Code.Should().Be(QuizErrorCodes.NotReady);
        await _chat.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default);
    }

    [Theory]
    [InlineData("Atlantis", "No indexed guide covers")]
    [InlineData("Scandinavia", "is several countries")]
    public async Task RefusesACountryItCannotPlace(string country, string reason)
    {
        var result = await Handler().Handle(Command(country), CancellationToken.None);

        result.Error.Code.Should().Be(QuizErrorCodes.UnknownCountry);
        result.Error.Message.Should().Contain(reason);
    }

    [Fact]
    public async Task IsOff_WhenTurnedOff()
    {
        _settings.Enabled = false;

        (await Handler().Handle(Command(), CancellationToken.None)).Error.Code.Should().Be(QuizErrorCodes.Disabled);
    }

    private static StartQuizRoundCommand Command(string? country = null, AiQuizDifficulty? difficulty = null) =>
        new(100, 1, country, false, difficulty);

    /// <summary>A rated picture that makes one question, drawn for its kind.</summary>
    private void Rated(AiQuizQuestionKind kind, string answer, AiQuizDifficulty difficulty, string country = "estonia")
    {
        var picture = QuizRounds.Picture("Estonian bollards carry a red reflector on a black band.", country);
        _chunks.Add(picture);
        _items.DrawAsync(Arg.Is<QuizItemDraw>(draw => draw.Kind == kind), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AiQuizItem>>([Item(picture, kind, answer, difficulty)]));
    }

    private static AiQuizItem Item(KnowledgeHit picture, AiQuizQuestionKind kind, string answer, AiQuizDifficulty difficulty = AiQuizDifficulty.Easy) =>
        AiQuizItem.Create(picture.Id, 1, QuizPictureRater.ContentHashOf(picture), picture.Country, picture.SourceType, Model, QuizRounds.AskedAt,
            [AiQuizItemQuestion.Create(kind, answer, ["Decoy 1", "Decoy 2", "Decoy 3"], difficulty)]);

    private StartQuizRoundHandler Handler()
    {
        var retrieval = Options.Create(new AiRetrievalConfiguration());
        var countries = new KnowledgeCountryDirectory(_index, retrieval, TimeProvider.System, NullLogger<KnowledgeCountryDirectory>.Instance);
        var overlap = new MainGuideOverlap(_index, countries, retrieval, NullLogger<MainGuideOverlap>.Instance);
        var rater = new QuizPictureRater(_chat, _catalog, _relay, _budget, _items, Options.Create(_ai), Options.Create(_settings),
            TimeProvider.System, NullLogger<QuizPictureRater>.Instance);

        return new StartQuizRoundHandler(_index, _items, _rounds, rater, overlap, countries, Options.Create(_settings), Options.Create(_ai),
            TimeProvider.System, NullLogger<StartQuizRoundHandler>.Instance);
    }
}
