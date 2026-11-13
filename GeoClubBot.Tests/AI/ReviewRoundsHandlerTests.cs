using System.Text.Json;
using Configuration;
using Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Refit;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Conversations;
using UseCases.UseCases.AI.Retrieval;
using UseCases.UseCases.AI.Review;
using UseCases.UseCases.AI.Tools;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// The round review: the member's own game found on the challenge's results, each round placed, the
/// guides searched per round in one request, and the model asked once with the rounds and excerpts.
/// </summary>
public sealed class ReviewRoundsHandlerTests
{
    private const ulong Discord = 42;
    private const string GeoGuessrId = "user-1";
    private const string Token = "AbCdEf123456";

    private readonly IGeoGuessrUserRepository _users = Substitute.For<IGeoGuessrUserRepository>();
    private readonly IClubChallengeRepository _challenges = Substitute.For<IClubChallengeRepository>();
    private readonly IGeoGuessrClient _client = Substitute.For<IGeoGuessrClient>();
    private readonly IRegionBoundaries _boundaries = Substitute.For<IRegionBoundaries>();
    private readonly IStreetViewGuesser _guesser = Substitute.For<IStreetViewGuesser>();
    private readonly IEmbedder _embedder = Substitute.For<IEmbedder>();
    private readonly IKnowledgeIndex _index = Substitute.For<IKnowledgeIndex>();
    private readonly IChatModelClient _chat = Substitute.For<IChatModelClient>();
    private readonly IChatModelCatalog _catalog = Substitute.For<IChatModelCatalog>();
    private readonly IAiBudgetRepository _budget = Substitute.For<IAiBudgetRepository>();
    private readonly AiRoundReviewConfiguration _settings = new();
    private readonly AiConfiguration _ai = new() { Active = true, OpenRouter = { DailyRequestBudget = 100 } };
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 13, 20, 0, 0, TimeSpan.Zero));

    public ReviewRoundsHandlerTests()
    {
        _users.ReadUserByDiscordUserIdAsync(Discord, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<GeoGuessrUser?>(GeoGuessrUser.Create(GeoGuessrId, "Player", Discord)));
        _budget.TryReserveRequestsAsync(Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));
        _catalog.ReadChainAsync(Arg.Any<ChatModelRequirements>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<string>>(["test/model"]));
        _chat.CompleteAsync(Arg.Any<AiChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<AiChatResponse>.Success(new AiChatResponse("Round 1: the white pole band gives Vietnam away [1].", "test/model", new AiTokenUsage(10, 5))));
        _embedder.EmbedAsync(Arg.Any<IReadOnlyList<EmbeddingInput>>(), Arg.Any<CancellationToken>())
            .Returns(call => Result<IReadOnlyList<ReadOnlyMemory<float>>>.Success(
                [.. ((IReadOnlyList<EmbeddingInput>)call[0]).Select((_, i) => (ReadOnlyMemory<float>)new float[] { i + 1 })]));
        _index.SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<KnowledgeHit>>([]));
        _index.ListCountriesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<string>>(["vietnam", "china", "thailand"]));
        _boundaries.LocateAsync(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<RegionPlace?>(null));
        _boundaries.LocateAsync(21.8, 106.7, Arg.Any<CancellationToken>()).Returns(Task.FromResult<RegionPlace?>(new RegionPlace("VN", "Lạng Sơn")));
        _boundaries.LocateAsync(23.5, 108.3, Arg.Any<CancellationToken>()).Returns(Task.FromResult<RegionPlace?>(new RegionPlace("CN", "Guangxi")));
    }

    [Fact]
    public async Task ReviewsTheMembersGame_PlacingRoundsAndGuesses_AndAskingTheModelOnce()
    {
        var hit = Hit("vietnam", "In Lạng Sơn the poles carry a white band.");
        _index.SearchAsync(Arg.Is<KnowledgeQuery>(query => query.Country == "vietnam"), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<KnowledgeHit>>([hit]));
        Highscores(Token, Game(GeoGuessrId,
            [Round(21.8, 106.7, "vn"), Round(15.0, 101.0, "th")],
            [Guess(23.5, 108.3, 412_400, 3870), Guess(15.1, 101.2, 8_260, 4981, "th")]));

        var result = await Handler().Handle(new ReviewRoundsCommand(Discord, $"https://www.geoguessr.com/challenge/{Token}"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        var review = result.Value;
        review.ChallengeToken.Should().Be(Token);
        review.Rounds.Should().HaveCount(2);
        review.Rounds[0].Should().BeEquivalentTo(new { CountryCode = "VN", Region = "Lạng Sơn", GuessCountryCode = "CN", GuessRegion = "Guangxi", DistanceKm = 412.4, Points = 3870 });
        review.Rounds[0].MissedCountry.Should().BeTrue();
        review.Rounds[1].CountryCode.Should().Be("TH");
        review.Rounds[1].MissedCountry.Should().BeFalse();
        review.Missed.Should().Be(1);

        review.Answer.Text.Should().Contain("white pole band");
        review.Answer.Sources.Should().ContainSingle().Which.Url.Should().Be(hit.SourceUrl);
        review.Answer.ModelUsed.Should().Be("test/model");

        // One embedding request for both rounds' searches; the budget claimed once, for two requests.
        await _embedder.Received(1).EmbedAsync(Arg.Is<IReadOnlyList<EmbeddingInput>>(inputs => inputs.Count == 2), Arg.Any<CancellationToken>());
        await _budget.Received(1).TryReserveRequestsAsync(Arg.Any<DateOnly>(), 2, 100, Arg.Any<CancellationToken>());
        await _chat.Received(1).CompleteAsync(
            Arg.Is<AiChatRequest>(request => request.Messages[request.Messages.Count - 1].ToPlainText().Contains("Round 1: Vietnam (Lạng Sơn). I guessed China (Guangxi), 412 km away, 3,870 points — wrong country.")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReviewsTheLatestDailyChallengeTheMemberPlayed_WhenNoneIsNamed()
    {
        _challenges.ReadLatestClubChallengeLinksAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<ClubChallengeLink> { ClubChallengeLink.Create("Easy", 1, "easy-token"), ClubChallengeLink.Create("Hard", 2, "hard-token") }));
        Highscores("easy-token", Game("someone-else", [Round(21.8, 106.7, "vn")], [Guess(21.8, 106.7, 10, 5000, "vn")]));
        Highscores("hard-token", Game(GeoGuessrId, [Round(21.8, 106.7, "vn")], [Guess(23.5, 108.3, 412_400, 3870)]));

        var result = await Handler().Handle(new ReviewRoundsCommand(Discord, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        result.Value.ChallengeToken.Should().Be("hard-token");
    }

    [Fact]
    public async Task PlacesARound_ByTheBoundaries_WhenGeoGuessrGivesNoCountry()
    {
        Highscores(Token, Game(GeoGuessrId, [Round(21.8, 106.7, null)], [Guess(null, null, null, 0)]));

        var result = await Handler().Handle(new ReviewRoundsCommand(Discord, Token), CancellationToken.None);

        result.Value.Rounds.Single().Should().BeEquivalentTo(new { CountryCode = "VN", Region = "Lạng Sơn", GuessCountryCode = (string?)null, DistanceKm = (double?)null });
        result.Value.Rounds.Single().MissedCountry.Should().BeTrue("no guess is a miss");
    }

    [Theory]
    [InlineData("not a link", RoundReviewErrorCodes.NoChallenge)]
    public async Task RefusesWhatIsNoChallenge(string challenge, string code)
    {
        var result = await Handler().Handle(new ReviewRoundsCommand(Discord, challenge), CancellationToken.None);

        result.Error.Code.Should().Be(code);
        await _client.DidNotReceiveWithAnyArgs().ReadHighscoresAsync(default!, default!, default);
    }

    [Fact]
    public async Task RefusesAMember_WithoutALinkedAccount()
    {
        _users.ReadUserByDiscordUserIdAsync(Discord, Arg.Any<CancellationToken>()).Returns(Task.FromResult<GeoGuessrUser?>(null));

        var result = await Handler().Handle(new ReviewRoundsCommand(Discord, Token), CancellationToken.None);

        result.Error.Code.Should().Be(RoundReviewErrorCodes.NotLinked);
    }

    [Fact]
    public async Task SaysSo_WhenTheMemberIsNotOnTheResults_OrTheResultsCarryNoRounds()
    {
        Highscores(Token, Game("someone-else", [Round(21.8, 106.7, "vn")], [Guess(21.8, 106.7, 10, 5000, "vn")]));
        (await Handler().Handle(new ReviewRoundsCommand(Discord, Token), CancellationToken.None)).Error.Code.Should().Be(RoundReviewErrorCodes.NotPlayed);

        Highscores(Token, Game(GeoGuessrId, rounds: null, guesses: null));
        (await Handler().Handle(new ReviewRoundsCommand(Discord, Token), CancellationToken.None)).Error.Code.Should().Be(RoundReviewErrorCodes.NoRounds);

        await _budget.DidNotReceiveWithAnyArgs().TryReserveRequestsAsync(default, default, default, default);
    }

    [Fact]
    public async Task ReportsResultsThatCannotBeRead_WithoutThrowing()
    {
        _client.ReadHighscoresAsync(Arg.Any<string>(), Arg.Any<ReadHighscoresQueryParams>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("502"));

        var result = await Handler().Handle(new ReviewRoundsCommand(Discord, Token), CancellationToken.None);

        result.Error.Code.Should().Be(RoundReviewErrorCodes.ResultsUnavailable);
    }

    [Fact]
    public async Task StopsAtTheAllowance_BeforeAnythingIsSpent()
    {
        Highscores(Token, Game(GeoGuessrId, [Round(21.8, 106.7, "vn")], [Guess(23.5, 108.3, 412_400, 3870)]));
        _budget.TryReserveRequestsAsync(Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));

        var result = await Handler().Handle(new ReviewRoundsCommand(Discord, Token), CancellationToken.None);

        result.Error.Code.Should().Be(AiAnswerErrors.BudgetExhaustedCode);
        await _embedder.DidNotReceiveWithAnyArgs().EmbedAsync(default!, default);
        await _chat.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default);
    }

    [Fact]
    public async Task ReadsGeoGuessrsRealHighscores_RoundsWithTheirCountry_GuessesWithout()
    {
        // GET /v3/results/highscores/{token} as GeoGuessr answered it on 2026-11-13 (two players kept,
        // their names replaced), read the way Refit reads it. Rounds carry streakLocationCode; guesses
        // carry null there, so the guessed country is always the boundaries' to find.
        var json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "AI", "geoguessr-challenge-highscores-20261113.json"));
        var highscores = JsonSerializer.Deserialize<ChallengeResultHighscoresDto>(json, SystemTextJsonContentSerializer.GetDefaultJsonSerializerOptions())!;
        var game = highscores.Items[0].Game;
        game.Rounds.Should().HaveCount(5).And.OnlyContain(round => round.Lat != null && round.Lng != null && round.StreakLocationCode != null);
        game.Player.Guesses.Should().HaveCount(5).And.OnlyContain(guess => guess.Lat != null && guess.DistanceInMeters != null && guess.RoundScoreInPoints != null && guess.StreakLocationCode == null);

        game.Player.Id = GeoGuessrId;
        _client.ReadHighscoresAsync(Token, Arg.Any<ReadHighscoresQueryParams>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(highscores));
        _boundaries.LocateAsync(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<RegionPlace?>(new RegionPlace("KE", "Nairobi")));

        var result = await Handler().Handle(new ReviewRoundsCommand(Discord, Token), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        result.Value.Rounds.Should().HaveCount(5);
        result.Value.Rounds.Select(round => round.CountryCode).Should().Equal([.. game.Rounds!.Select(round => round.StreakLocationCode!.ToUpperInvariant())]);
        result.Value.Rounds.Should().OnlyContain(round => round.GuessCountryCode == "KE" && round.Points != null);
    }

    [Fact]
    public async Task AsksGeoNN_ForEachPanorama_AsTheGameShowedIt_AndTellsTheModelHowToUseIt()
    {
        _guesser.IsAvailable.Returns(true);
        _guesser.GuessAsync(Arg.Any<PanoramaView>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PanoramaGuess?>(new PanoramaGuess(21.9, 106.6, [new("VN", 0.94), new("CN", 0.03), new("LA", 0.01), new("TH", 0.01)], "v2")));
        var game = Game(GeoGuessrId, [Round(21.8, 106.7, "vn")], [Guess(23.5, 108.3, 412_400, 3870)]);
        game.ForbidRotating = true;
        game.Rounds![0].PanoId = "7470683379453331676366314D436E61756671565567";
        game.Rounds[0].Heading = 225;
        game.Rounds[0].Pitch = -33.5;
        game.Rounds[0].Zoom = 1.9;
        Highscores(Token, game);

        var result = await Handler().Handle(new ReviewRoundsCommand(Discord, Token), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        await _guesser.Received(1).GuessAsync(
            new PanoramaView("tph3yE31gcf1MCnaufqVUg", PanoramaViewMode.FixedView, 225, -33.5, 1.9), Arg.Any<CancellationToken>());
        var reading = result.Value.Rounds.Single().Reading!;
        reading.Countries.Should().HaveCount(3, "CountriesShown");
        reading.DistanceKm.Should().BeApproximately(14.9, 0.5);
        result.Value.Answer.Notices.Should().ContainSingle().Which.Should().Contain("GeoNN (v2)");
        await _chat.Received(1).CompleteAsync(
            Arg.Is<AiChatRequest>(request => request.Messages[request.Messages.Count - 1].ToPlainText().Contains("GeoNN read the panorama as Vietnam 94%, China 3%, Laos 1% (its guess 15 km off).")
                                             && request.Messages[request.Messages.Count - 1].ToPlainText().Contains("never cite it")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReviewsWithoutGeoNN_WhenItIsNotSetUp_OrHasNoReading()
    {
        Highscores(Token, Game(GeoGuessrId, [Round(21.8, 106.7, "vn")], [Guess(23.5, 108.3, 412_400, 3870)]));
        var unset = await Handler().Handle(new ReviewRoundsCommand(Discord, Token), CancellationToken.None);
        await _guesser.DidNotReceiveWithAnyArgs().GuessAsync(default!, default);

        _guesser.IsAvailable.Returns(true);
        _guesser.GuessAsync(Arg.Any<PanoramaView>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<PanoramaGuess?>(null));
        Highscores(Token, Game(GeoGuessrId, [new ChallengeResultRoundDto { Lat = 21.8, Lng = 106.7, PanoId = "abc" }], [Guess(23.5, 108.3, 412_400, 3870)]));
        var unanswered = await Handler().Handle(new ReviewRoundsCommand(Discord, Token), CancellationToken.None);

        foreach (var result in new[] { unset, unanswered })
        {
            result.IsSuccess.Should().BeTrue();
            result.Value.Rounds.Single().Reading.Should().BeNull();
            result.Value.Answer.Notices.Should().BeEmpty();
        }

        await _guesser.Received(1).GuessAsync(Arg.Is<PanoramaView>(view => view.PanoId == "abc" && view.Mode == PanoramaViewMode.FullPanorama), Arg.Any<CancellationToken>());
        await _chat.DidNotReceive().CompleteAsync(Arg.Is<AiChatRequest>(request => request.Messages[request.Messages.Count - 1].ToPlainText().Contains("GeoNN")), Arg.Any<CancellationToken>());
    }

    private ReviewRoundsHandler Handler()
    {
        var retrieval = Options.Create(new AiRetrievalConfiguration());
        var tools = Options.Create(new AiToolsConfiguration());
        var countries = new KnowledgeCountryDirectory(_index, retrieval, _clock, NullLogger<KnowledgeCountryDirectory>.Instance);
        var toolbox = new AnswerToolbox(
            _embedder, _index, Substitute.For<ISpeciesRangeReader>(), Substitute.For<ISpeciesRangeMap>(),
            Substitute.For<IRegionBoundaries>(), Substitute.For<IRegionMapDrawer>(),
            new MainGuideOverlap(_index, countries, retrieval, NullLogger<MainGuideOverlap>.Instance),
            countries, retrieval, tools, NullLogger<AnswerToolbox>.Instance);
        var loop = new AiAnswerLoop(_chat, _catalog, _budget, toolbox, Options.Create(_ai), tools, _clock, NullLogger<AiAnswerLoop>.Instance);

        var factory = Substitute.For<IGeoGuessrClientFactory>();
        factory.CreateClient(Arg.Any<Guid>()).Returns(_client);
        var geoGuessr = Options.Create(new GeoGuessrConfiguration
        {
            ActivityNcfaToken = "token",
            UserProfileNcfaToken = "token",
            SyncSchedule = "0 0 * * * ?",
            Clubs = [new GeoGuessrClubEntry { ClubId = Guid.NewGuid(), IsMain = true, NcfaToken = "token" }]
        });

        return new ReviewRoundsHandler(
            _users, _challenges, factory, _boundaries, _guesser, _embedder, _index, countries, loop, _budget,
            geoGuessr, Options.Create(_ai), Options.Create(_settings), _clock, NullLogger<ReviewRoundsHandler>.Instance);
    }

    private void Highscores(string token, ChallengeResultGameDto game) =>
        _client.ReadHighscoresAsync(token, Arg.Any<ReadHighscoresQueryParams>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChallengeResultHighscoresDto { Items = [new ChallengeResultItemDto { Game = game }] }));

    private static ChallengeResultGameDto Game(string playerId, List<ChallengeResultRoundDto>? rounds, List<ChallengeResultGuessDto>? guesses) =>
        new()
        {
            Rounds = rounds,
            Player = new ChallengeResultPlayerDto
            {
                Id = playerId,
                Nick = "Player",
                TotalScoreInPoints = 8851,
                TotalDistance = new ChallengeResultPlayerDistanceDto { Meters = new ChallengeResultPlayerDistanceMetersDto { Amount = "420000", Unit = "m" } },
                Guesses = guesses
            }
        };

    private static ChallengeResultRoundDto Round(double lat, double lng, string? code) => new() { Lat = lat, Lng = lng, StreakLocationCode = code };

    private static ChallengeResultGuessDto Guess(double? lat, double? lng, double? metres, int points, string? code = null) =>
        new() { Lat = lat, Lng = lng, DistanceInMeters = metres, RoundScoreInPoints = points, StreakLocationCode = code, SkippedRound = lat is null };

    private static KnowledgeHit Hit(string country, string text) =>
        new(Guid.NewGuid(), 0.5f, KnowledgeChunkKind.Text, text, "https://docs.google.com/document/d/x#heading=h.1", null,
            "Vietnam region guide", country, "Vietnam region guide > Poles", null, 0, "gdoc", "x", "p1");
}
