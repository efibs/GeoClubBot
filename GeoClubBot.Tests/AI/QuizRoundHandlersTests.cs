using Configuration;
using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Quiz;
using UseCases.UseCases.AI.Retrieval;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>Guessing, revealing, the leaderboard and the nightly rating run of the quiz (#491).</summary>
public sealed class QuizRoundHandlersTests
{
    private const string Model = "vendor/vision:free";

    private readonly IAiQuizRoundRepository _rounds = Substitute.For<IAiQuizRoundRepository>();
    private readonly FakeTimeProvider _time = new(QuizRounds.AskedAt.AddMinutes(1));
    private readonly AiQuizConfiguration _settings = new();

    public QuizRoundHandlersTests()
    {
        _rounds.ReadScoresAsync(Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<QuizScoreLine>>([]));
    }

    [Fact]
    public async Task Guess_RecordsTheGuess_AndAddsItToTheMembersScores()
    {
        var round = Stored(QuizRounds.Round(difficulty: AiQuizDifficulty.Hard));
        _rounds.ReadScoresAsync(null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<QuizScoreLine>>([new QuizScoreLine(7, 5, 2, 4)]));

        var result = await Guess().Handle(new GuessQuizCommand(round.Id, 7, round.CorrectIndex), CancellationToken.None);

        result.Value.Guess.Points.Should().Be(3);
        result.Value.AllTime.Should().Be(new QuizScoreLine(7, 8, 3, 5));
        result.Value.Recent.Should().Be(new QuizScoreLine(7, 3, 1, 1));
        await _rounds.Received(1).ReadScoresAsync(_time.GetUtcNow().AddDays(-7), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Guess_RefusesASecondGuess_NamingTheFirst()
    {
        var round = Stored(QuizRounds.Round());
        round.Guess(7, 0, QuizRounds.AskedAt);

        var result = await Guess().Handle(new GuessQuizCommand(round.Id, 7, round.CorrectIndex), CancellationToken.None);

        result.Error.Code.Should().Be(QuizErrorCodes.AlreadyGuessed);
        result.Error.Message.Should().Contain("Latvia");
    }

    [Fact]
    public async Task Guess_RefusesOnceTheAnswerIsOut()
    {
        var round = Stored(QuizRounds.Round());
        round.Reveal(QuizRounds.AskedAt);

        (await Guess().Handle(new GuessQuizCommand(round.Id, 7, 0), CancellationToken.None)).Error.Code.Should().Be(QuizErrorCodes.AlreadyRevealed);
    }

    [Theory]
    [InlineData(1UL, false, true)]
    [InlineData(2UL, true, true)]
    [InlineData(2UL, false, false)]
    public async Task Reveal_IsForTheAskerAndAdmins(ulong userId, bool isAdmin, bool allowed)
    {
        var round = Stored(QuizRounds.Round(askedBy: 1));

        var result = await new RevealQuizRoundHandler(_rounds, _time).Handle(new RevealQuizRoundCommand(round.Id, userId, isAdmin), CancellationToken.None);

        result.IsSuccess.Should().Be(allowed);
        round.IsRevealed.Should().Be(allowed);
    }

    [Fact]
    public async Task RevealDue_ShowsEachAnswer_AndMarksEvenTheOnesItCannotShow()
    {
        var posted = QuizRounds.Round();
        posted.AttachMessage(555);
        var neverPosted = QuizRounds.Round();
        _rounds.ReadDueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AiQuizRound>>([posted, neverPosted]));
        var messages = Substitute.For<IAiQuizMessages>();
        messages.ShowAnswerAsync(Arg.Any<AiQuizRound>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));

        var result = await new RevealDueQuizRoundsHandler(_rounds, messages, _time, NullLogger<RevealDueQuizRoundsHandler>.Instance)
            .Handle(new RevealDueQuizRoundsCommand(), CancellationToken.None);

        result.Value.Should().Be(2);
        posted.IsRevealed.Should().BeTrue("a message that cannot be edited is not tried every minute");
        neverPosted.IsRevealed.Should().BeTrue();
        await messages.Received(1).ShowAnswerAsync(posted, Arg.Any<CancellationToken>());
        await messages.DidNotReceive().ShowAnswerAsync(neverPosted, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void RankOf_SharesRanksOnTies()
    {
        IReadOnlyList<QuizScoreLine> scores = [new(1, 9, 4, 5), new(2, 9, 5, 6), new(3, 4, 2, 2)];

        ReadQuizLeaderboardHandler.RankOf(scores, 2)!.Value.Rank.Should().Be(1);
        ReadQuizLeaderboardHandler.RankOf(scores, 3)!.Value.Rank.Should().Be(3);
        ReadQuizLeaderboardHandler.RankOf(scores, 4).Should().BeNull();
    }

    [Fact]
    public async Task RateRun_RatesOnlyPicturesWithoutACurrentRating_WithinItsShare()
    {
        var index = Substitute.For<IKnowledgeIndex>();
        var items = Substitute.For<IAiQuizItemRepository>();
        var chat = Substitute.For<IChatModelClient>();
        var catalog = Substitute.For<IChatModelCatalog>();
        var budget = Substitute.For<IAiBudgetRepository>();
        var ai = new AiConfiguration { Active = true };

        var rated = QuizRounds.Picture("Estonian bollards carry a red reflector on a black band, unlike Latvian ones.");
        var unrated = QuizRounds.Picture("Estonian snow poles are striped red and white, and stand on both sides.");
        var heading = QuizRounds.Picture("Bollards");
        var noCountry = QuizRounds.Picture("A clue long enough to count as one, from no country the table knows.", country: null);
        index.SampleAsync(Arg.Any<KnowledgeSample>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<KnowledgeHit>>([rated, unrated, heading, noCountry]));
        items.ReadContentHashesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string> { [rated.Id] = QuizPictureRater.ContentHashOf(rated) }));
        catalog.ReadChainAsync(Arg.Any<ChatModelRequirements>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<string>>([Model]));
        budget.TryReserveRequestsAsync(Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));
        chat.CompleteAsync(Arg.Any<AiChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<AiChatResponse>.Success(new AiChatResponse("Country shown: no\nCountry: no\nRegion: no\nClue: no", Model, new AiTokenUsage(1, 1)))));

        var rater = new QuizPictureRater(chat, catalog, Substitute.For<IImageRelay>(), budget, items, Options.Create(ai), Options.Create(_settings),
            _time, NullLogger<QuizPictureRater>.Instance);
        index.ListCountriesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<string>>(["estonia"]));
        var countries = new KnowledgeCountryDirectory(index, Options.Create(new AiRetrievalConfiguration()), TimeProvider.System,
            NullLogger<KnowledgeCountryDirectory>.Instance);
        var draw = new QuizPictureDraw(index, items, countries, rater);
        var result = await new RateQuizPicturesHandler(draw, rater, Options.Create(ai), Options.Create(_settings), NullLogger<RateQuizPicturesHandler>.Instance)
            .Handle(new RateQuizPicturesCommand(), CancellationToken.None);

        result.Value.Should().Be(new QuizRatingReport(1, 1, 0, 0, 0, false, false));
        await items.Received(1).ReplaceAsync(Arg.Is<AiQuizItem>(item => item.ChunkId == unrated.Id && item.Questions.Count == 0), Arg.Any<CancellationToken>());
        await budget.Received(1).TryReserveRequestsAsync(Arg.Any<DateOnly>(), 1, ai.OpenRouter.DailyRequestBudget * 20 / 100, Arg.Any<CancellationToken>());
        await index.Received().SampleAsync(Arg.Is<KnowledgeSample>(sample => sample.Country == "estonia"), Arg.Any<CancellationToken>());
    }

    private AiQuizRound Stored(AiQuizRound round)
    {
        _rounds.ReadAsync(round.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<AiQuizRound?>(round));
        return round;
    }

    private GuessQuizHandler Guess() => new(_rounds, Options.Create(_settings), _time);
}
