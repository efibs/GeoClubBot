using System.Text.Json;
using Configuration;
using Entities;
using FluentAssertions;
using Infrastructure.OutputAdapters.GeoGuessr;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Conversations;
using UseCases.UseCases.AI.Retrieval;
using UseCases.UseCases.AI.Review;
using UseCases.UseCases.AI.StreetView;
using UseCases.UseCases.AI.Tools;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// The round review: the game found (the member's own of a challenge, any game by its link, or a Street
/// View place from a Maps link), each round placed, the guides searched for its clues in one request,
/// each panorama checked against them by a vision model, and the model asked once, without tools, with
/// only the clues each round was seen to show (#480).
/// </summary>
public sealed class ReviewRoundsHandlerTests
{
    private const ulong Discord = 42;
    private const string GeoGuessrId = "user-1";
    private const string Token = "AbCdEf123456";
    private const string PanoHex = "7470683379453331676366314D436E61756671565567";
    private const string PanoId = "tph3yE31gcf1MCnaufqVUg";

    private const string VisionReply = """
        1: yes — picture 2, right roadside: concrete poles with a white band
        2: no
        Seen: red soil by the road (picture 1)
        Best picture: 2
        """;

    private readonly IGeoGuessrUserRepository _users = Substitute.For<IGeoGuessrUserRepository>();
    private readonly IClubChallengeRepository _challenges = Substitute.For<IClubChallengeRepository>();
    private readonly IGeoGuessrClient _client = Substitute.For<IGeoGuessrClient>();
    private readonly IRegionBoundaries _boundaries = Substitute.For<IRegionBoundaries>();
    private readonly IStreetViewGuesser _guesser = Substitute.For<IStreetViewGuesser>();
    private readonly IStreetViewImagery _imagery = Substitute.For<IStreetViewImagery>();
    private readonly IMapsLinkResolver _mapsLinks = Substitute.For<IMapsLinkResolver>();
    private readonly IImageRelay _relay = Substitute.For<IImageRelay>();
    private readonly IEmbedder _embedder = Substitute.For<IEmbedder>();
    private readonly IKnowledgeIndex _index = Substitute.For<IKnowledgeIndex>();
    private readonly IChatModelClient _chat = Substitute.For<IChatModelClient>();
    private readonly IChatModelCatalog _catalog = Substitute.For<IChatModelCatalog>();
    private readonly IAiBudgetRepository _budget = Substitute.For<IAiBudgetRepository>();
    private readonly AiRoundReviewConfiguration _settings = new();
    private readonly AiConfiguration _ai = new() { Active = true, OpenRouter = { DailyRequestBudget = 100 } };
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 13, 20, 0, 0, TimeSpan.Zero));
    private readonly List<AiChatRequest> _asked = [];

    public ReviewRoundsHandlerTests()
    {
        _users.ReadUserByDiscordUserIdAsync(Discord, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<GeoGuessrUser?>(GeoGuessrUser.Create(GeoGuessrId, "Player", Discord)));
        _budget.TryReserveRequestsAsync(Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));
        _catalog.ReadChainAsync(Arg.Any<ChatModelRequirements>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<string>>(["test/model"]));
        _chat.CompleteAsync(Arg.Any<AiChatRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var request = (AiChatRequest)call[0];
            lock (_asked)
            {
                _asked.Add(request);
            }

            return Result<AiChatResponse>.Success(IsVision(request)
                ? new AiChatResponse(VisionReply, "vision/model", new AiTokenUsage(900, 40))
                : new AiChatResponse("Round 1: the white pole band gives Vietnam away [1].", "test/model", new AiTokenUsage(10, 5)));
        });
        _embedder.EmbedAsync(Arg.Any<IReadOnlyList<EmbeddingInput>>(), Arg.Any<CancellationToken>())
            .Returns(call => Result<IReadOnlyList<ReadOnlyMemory<float>>>.Success(
                [.. ((IReadOnlyList<EmbeddingInput>)call[0]).Select((_, i) => (ReadOnlyMemory<float>)new float[] { i + 1 })]));
        _index.SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<KnowledgeHit>>([]));
        _index.ListCountriesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<string>>(["vietnam", "china", "thailand", "united states"]));
        _boundaries.LocateAsync(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<RegionPlace?>(null));
        _boundaries.LocateAsync(21.8, 106.7, Arg.Any<CancellationToken>()).Returns(Task.FromResult<RegionPlace?>(new RegionPlace("VN", "Lạng Sơn")));
        _boundaries.LocateAsync(23.5, 108.3, Arg.Any<CancellationToken>()).Returns(Task.FromResult<RegionPlace?>(new RegionPlace("CN", "Guangxi")));
        _imagery.ReadViewsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<StreetViewAngle>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<StreetViewPicture>>(
                [.. ((IReadOnlyList<StreetViewAngle>)call[1]).Select(angle => new StreetViewPicture([1, 2, 3], "image/jpeg", angle))]));
        _relay.IsEnabled.Returns(true);
        _relay.StoreAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<string>.Success("https://bot.example/api/v1/ai/images/view.jpg")));
    }

    [Fact]
    public async Task ChecksEachRoundsPanorama_AndOffersTheModelOnlyTheCluesItShowed()
    {
        var poles = Hit("vietnam", "Poles", "Concrete poles with a white band are found in the north.");
        var lines = Hit("vietnam", "Road lines", "Yellow centre lines on every road.");
        Search("vietnam", poles, lines);
        Highscores(Token, Game(GeoGuessrId,
            [Round(21.8, 106.7, "vn", PanoHex), Round(15.0, 101.0, "th")],
            [Guess(23.5, 108.3, 412_400, 3870), Guess(15.1, 101.2, 8_260, 4981, "th")]));

        var result = await Handler().Handle(new ReviewRoundsCommand(Discord, $"https://www.geoguessr.com/challenge/{Token}"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        var review = result.Value;
        review.Subject.Should().Be(new ReviewSubject(ReviewSubjectKind.Challenge, Token, "Player", IsOwnGame: true));
        review.Rounds.Should().HaveCount(2);
        review.Rounds[0].Should().BeEquivalentTo(new { CountryCode = "VN", Region = "Lạng Sơn", GuessCountryCode = "CN", GuessRegion = "Guangxi", DistanceKm = 412.4, Points = 3870 });
        review.Missed.Should().Be(1);

        // The round with a panorama was looked at in four quarter-turns, and checked against its clues.
        await _imagery.Received(1).ReadViewsAsync(PanoId, Arg.Is<IReadOnlyList<StreetViewAngle>>(angles => angles.Count == 4), Arg.Any<CancellationToken>());
        var vision = _asked.Single(IsVision);
        vision.Messages[^1].ToPlainText().Should().Contain("1. Region guide > Poles: Concrete poles with a white band").And.Contain("The player guessed China.");

        var evidence = review.Rounds[0].Evidence!;
        evidence.Checked.Should().BeTrue();
        evidence.Seen.Should().ContainSingle().Which.Should().Be(new SeenExcerpt(1, "picture 2, right roadside: concrete poles with a white band", false));
        evidence.AlsoSeen.Should().Equal("red soil by the road (picture 1)");
        evidence.ToLookFor.Should().BeEmpty("a country-wide clue was seen");
        review.Rounds[1].Evidence!.Checked.Should().BeFalse("the round names no panorama");

        // The review is asked once, without tools, with what each round was seen to show.
        var answer = _asked.Single(request => !IsVision(request));
        answer.Tools.Should().BeNullOrEmpty();
        var question = answer.Messages[^1].ToPlainText();
        question.Should().Contain("Round 1: Vietnam (Lạng Sơn). Guessed China (Guangxi), 412 km away, 3,870 points — wrong country.");
        question.Should().Contain("Seen in its panorama: [1] picture 2, right roadside: concrete poles with a white band.");
        question.Should().Contain("Round 2: Thailand. Guessed Thailand, 8.3 km away, 4,981 points — right country, well played.");

        review.Answer.Text.Should().Contain("white pole band");
        review.Answer.Sources.Should().HaveCount(2);
        review.Answer.Sources[0].Url.Should().Be(poles.SourceUrl);

        // The first round's most telling view is posted, its line leading to the round in Google Maps.
        var view = review.Answer.Images.Should().ContainSingle(image => image.Title!.StartsWith("Round 1")).Subject;
        view.ImageUrl.Should().Be("https://bot.example/api/v1/ai/images/view.jpg");
        view.SourceUrl.Should().StartWith("https://www.google.com/maps/@?api=1&map_action=pano&pano=" + PanoId);
        review.Answer.Sources[1].Should().Be(new AiAnswerSource(2, "Round 1: Vietnam (Lạng Sơn) (Google Maps)", view.SourceUrl, IsImage: true));
        review.Answer.Notices.Should().ContainSingle().Which.Should().Contain("free vision model");

        // Up front: one embedding request for both rounds' searches and the chat request (2); then the
        // one vision request with a spare for a retry (2), the spare given back.
        await _embedder.Received(1).EmbedAsync(Arg.Any<IReadOnlyList<EmbeddingInput>>(), Arg.Any<CancellationToken>());
        await _budget.Received(2).TryReserveRequestsAsync(Arg.Any<DateOnly>(), 2, 100, Arg.Any<CancellationToken>());
        await _budget.Received(1).ReleaseRequestsAsync(Arg.Any<DateOnly>(), 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReviewsAnyPlayersGame_FromAGameLink_WithoutALinkedAccount()
    {
        _users.ReadUserByDiscordUserIdAsync(Discord, Arg.Any<CancellationToken>()).Returns(Task.FromResult<GeoGuessrUser?>(null));
        var game = Game("someone-else", [Round(21.8, 106.7, "vn", PanoHex)], [Guess(23.5, 108.3, 412_400, 3870)]);
        game.Player.Nick = "malta";
        game.MapName = "A Community World";
        game.State = "finished";
        _client.ReadGameAsync("dhmguJ9rQTqI6cmA", Arg.Any<CancellationToken>()).Returns(Task.FromResult<ChallengeResultGameDto?>(game));

        var result = await Handler().Handle(new ReviewRoundsCommand(Discord, "https://www.geoguessr.com/game/dhmguJ9rQTqI6cmA"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        result.Value.Subject.Should().Be(new ReviewSubject(ReviewSubjectKind.Game, "dhmguJ9rQTqI6cmA", "malta", IsOwnGame: false, "A Community World"));
        _asked.Single(request => !IsVision(request)).Messages[^1].ToPlainText()
            .Should().Contain("Review this GeoGuessr game, played by malta on A Community World (malta's game `dhmguJ9rQTqI6cmA`), round by round.")
            .And.Contain("This is malta's game, not mine: speak of malta.");
        await _client.DidNotReceiveWithAnyArgs().ReadHighscoresAsync(default!, default!, default);
    }

    [Fact]
    public async Task ReadsAResultsLink_AsAGameFirst_ThenAsTheMembersGameOfAChallenge()
    {
        _client.ReadGameAsync(Token, Arg.Any<CancellationToken>()).Returns(Task.FromResult<ChallengeResultGameDto?>(null));
        Highscores(Token, Game(GeoGuessrId, [Round(21.8, 106.7, "vn")], [Guess(23.5, 108.3, 412_400, 3870)]));

        var result = await Handler().Handle(new ReviewRoundsCommand(Discord, $"https://www.geoguessr.com/results/{Token}"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        result.Value.Subject.Kind.Should().Be(ReviewSubjectKind.Challenge);
        await _client.Received(1).ReadGameAsync(Token, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusesAGameStillBeingPlayed_WhoseLastRoundIsOnScreen()
    {
        var game = Game("someone-else", [Round(21.8, 106.7, "vn", PanoHex)], []);
        game.State = "started";
        _client.ReadGameAsync("dhmguJ9rQTqI6cmA", Arg.Any<CancellationToken>()).Returns(Task.FromResult<ChallengeResultGameDto?>(game));

        var result = await Handler().Handle(new ReviewRoundsCommand(Discord, "https://www.geoguessr.com/game/dhmguJ9rQTqI6cmA"), CancellationToken.None);

        result.Error.Code.Should().Be(RoundReviewErrorCodes.Unfinished);
        await _boundaries.DidNotReceiveWithAnyArgs().LocateAsync(default, default, default);
        await _budget.DidNotReceiveWithAnyArgs().TryReserveRequestsAsync(default, default, default, default);
    }

    [Fact]
    public async Task ExplainsAStreetViewPlace_FromAShortMapsLink()
    {
        const string full = "https://www.google.com/maps/@34.0519954,-99.9644448,3a,90y,51.57h,92.16t/data=!3m7!1e1!3m5!1s3kZJIREjaW8A1P039qpRvA!2e0!7i16384!8i8192";
        _mapsLinks.ResolveAsync("https://maps.app.goo.gl/dfn7LaomB9WMUtKu5", Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(full));
        _boundaries.LocateAsync(34.0519954, -99.9644448, Arg.Any<CancellationToken>()).Returns(Task.FromResult<RegionPlace?>(new RegionPlace("US", "Texas")));
        Search("united states", Hit("united states", "Soil", "Red soil is common in Oklahoma and north Texas."));
        _users.ReadUserByDiscordUserIdAsync(Discord, Arg.Any<CancellationToken>()).Returns(Task.FromResult<GeoGuessrUser?>(null));

        var result = await Handler().Handle(new ReviewRoundsCommand(Discord, "what gives this away? https://maps.app.goo.gl/dfn7LaomB9WMUtKu5"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        result.Value.Subject.Kind.Should().Be(ReviewSubjectKind.StreetView);
        var place = result.Value.Rounds.Single();
        place.Should().BeEquivalentTo(new { Played = false, CountryCode = "US", Region = "Texas", MissedCountry = false });
        await _imagery.Received(1).ReadViewsAsync("3kZJIREjaW8A1P039qpRvA",
            Arg.Is<IReadOnlyList<StreetViewAngle>>(angles => angles.Count == 4 && Math.Abs(angles[0].Heading - 51.57) < 0.01 && Math.Abs(angles[0].Pitch - 2.16) < 0.01),
            Arg.Any<CancellationToken>());
        _asked.Single(request => !IsVision(request)).Messages[^1].ToPlainText()
            .Should().Contain("Explain this Street View place: what gives away its country and region. It is in United States (Texas).");
        await _client.DidNotReceiveWithAnyArgs().ReadGameAsync(default!, default);
    }

    [Theory]
    [InlineData("https://www.google.com/maps/@34.05,-99.96,15z", "shows a map, not Street View")]
    [InlineData("https://maps.app.goo.gl/doesnotexist", "could not be opened")]
    public async Task SaysWhatIsWrong_WithAMapsLinkItCannotReview(string link, string message)
    {
        _mapsLinks.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(null));

        var result = await Handler().Handle(new ReviewRoundsCommand(Discord, link), CancellationToken.None);

        result.Error.Code.Should().Be(RoundReviewErrorCodes.NoPlace);
        result.Error.Message.Should().Contain(message);
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
        result.Value.Subject.Token.Should().Be("hard-token");
    }

    [Fact]
    public async Task PlacesARound_ByTheBoundaries_WhenGeoGuessrGivesNoCountry()
    {
        Highscores(Token, Game(GeoGuessrId, [Round(21.8, 106.7, null)], [Guess(null, null, null, 0)]));

        var result = await Handler().Handle(new ReviewRoundsCommand(Discord, $"https://www.geoguessr.com/challenge/{Token}"), CancellationToken.None);

        result.Value.Rounds.Single().Should().BeEquivalentTo(new { CountryCode = "VN", Region = "Lạng Sơn", GuessCountryCode = (string?)null, DistanceKm = (double?)null });
        result.Value.Rounds.Single().MissedCountry.Should().BeTrue("no guess is a miss");
    }

    [Fact]
    public async Task RefusesWhatIsNoLink()
    {
        var result = await Handler().Handle(new ReviewRoundsCommand(Discord, "not a link"), CancellationToken.None);

        result.Error.Code.Should().Be(RoundReviewErrorCodes.NoChallenge);
        await _client.DidNotReceiveWithAnyArgs().ReadHighscoresAsync(default!, default!, default);
    }

    [Fact]
    public async Task RefusesAChallenge_ToAMemberWithoutALinkedAccount()
    {
        _users.ReadUserByDiscordUserIdAsync(Discord, Arg.Any<CancellationToken>()).Returns(Task.FromResult<GeoGuessrUser?>(null));

        var result = await Handler().Handle(new ReviewRoundsCommand(Discord, $"https://www.geoguessr.com/challenge/{Token}"), CancellationToken.None);

        result.Error.Code.Should().Be(RoundReviewErrorCodes.NotLinked);
        result.Error.Message.Should().Contain("game link");
    }

    [Fact]
    public async Task SaysSo_WhenTheMemberIsNotOnTheResults_OrTheResultsCarryNoRounds()
    {
        var link = $"https://www.geoguessr.com/challenge/{Token}";
        Highscores(Token, Game("someone-else", [Round(21.8, 106.7, "vn")], [Guess(21.8, 106.7, 10, 5000, "vn")]));
        (await Handler().Handle(new ReviewRoundsCommand(Discord, link), CancellationToken.None)).Error.Code.Should().Be(RoundReviewErrorCodes.NotPlayed);

        Highscores(Token, Game(GeoGuessrId, rounds: null, guesses: null));
        (await Handler().Handle(new ReviewRoundsCommand(Discord, link), CancellationToken.None)).Error.Code.Should().Be(RoundReviewErrorCodes.NoRounds);

        await _budget.DidNotReceiveWithAnyArgs().TryReserveRequestsAsync(default, default, default, default);
    }

    [Fact]
    public async Task ReportsResultsThatCannotBeRead_WithoutThrowing()
    {
        _client.ReadHighscoresAsync(Arg.Any<string>(), Arg.Any<ReadHighscoresQueryParams>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("502"));
        _client.ReadGameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("502"));

        (await Handler().Handle(new ReviewRoundsCommand(Discord, $"https://www.geoguessr.com/challenge/{Token}"), CancellationToken.None))
            .Error.Code.Should().Be(RoundReviewErrorCodes.ResultsUnavailable);
        (await Handler().Handle(new ReviewRoundsCommand(Discord, "https://www.geoguessr.com/game/dhmguJ9rQTqI6cmA"), CancellationToken.None))
            .Error.Code.Should().Be(RoundReviewErrorCodes.ResultsUnavailable);
    }

    [Fact]
    public async Task StopsAtTheAllowance_BeforeAnythingIsSpent()
    {
        Highscores(Token, Game(GeoGuessrId, [Round(21.8, 106.7, "vn", PanoHex)], [Guess(23.5, 108.3, 412_400, 3870)]));
        _budget.TryReserveRequestsAsync(Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));

        var result = await Handler().Handle(new ReviewRoundsCommand(Discord, $"https://www.geoguessr.com/challenge/{Token}"), CancellationToken.None);

        result.Error.Code.Should().Be(AiAnswerErrors.BudgetExhaustedCode);
        await _embedder.DidNotReceiveWithAnyArgs().EmbedAsync(default!, default);
        await _imagery.DidNotReceiveWithAnyArgs().ReadViewsAsync(default!, default!, default);
        await _chat.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default);
    }

    [Fact]
    public async Task ReviewsWhatItKnows_WhenNoVisionModelCanLook()
    {
        Search("vietnam", Hit("vietnam", "Poles", "Concrete poles with a white band are found in the north."));
        _catalog.ReadChainAsync(Arg.Is<ChatModelRequirements>(requirements => requirements.NeedsImageInput), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>([]));
        Highscores(Token, Game(GeoGuessrId, [Round(21.8, 106.7, "vn", PanoHex)], [Guess(23.5, 108.3, 412_400, 3870)]));

        var result = await Handler().Handle(new ReviewRoundsCommand(Discord, $"https://www.geoguessr.com/challenge/{Token}"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        var evidence = result.Value.Rounds.Single().Evidence!;
        evidence.Checked.Should().BeFalse();
        evidence.ToLookFor.Should().Equal(1);
        _asked.Single().Messages[^1].ToPlainText()
            .Should().Contain("Its panorama could not be looked at, so nothing is known of what it showed. Clues of Vietnam to look for: [1].");
        result.Value.Answer.Notices.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadsGeoGuessrsRealHighscores_RoundsWithTheirCountry_GuessesWithout()
    {
        // GET /v3/results/highscores/{token} as GeoGuessr answered it on 2026-11-13 (two players kept,
        // their names replaced), read the way Refit reads it. Rounds carry streakLocationCode; guesses
        // carry null there, so the guessed country is always the boundaries' to find.
        var json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "AI", "geoguessr-challenge-highscores-20261113.json"));
        var highscores = JsonSerializer.Deserialize<ChallengeResultHighscoresDto>(json, GeoGuessrJsonContext.Default.Options)!;
        var game = highscores.Items[0].Game;
        game.Rounds.Should().HaveCount(5).And.OnlyContain(round => round.Lat != null && round.Lng != null && round.StreakLocationCode != null);
        game.Player.Guesses.Should().HaveCount(5).And.OnlyContain(guess => guess.Lat != null && guess.DistanceInMeters != null && guess.RoundScoreInPoints != null && guess.StreakLocationCode == null);

        game.Player.Id = GeoGuessrId;
        _client.ReadHighscoresAsync(Token, Arg.Any<ReadHighscoresQueryParams>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(highscores));
        _boundaries.LocateAsync(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<RegionPlace?>(new RegionPlace("KE", "Nairobi")));

        var result = await Handler().Handle(new ReviewRoundsCommand(Discord, $"https://www.geoguessr.com/challenge/{Token}"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        result.Value.Rounds.Should().HaveCount(5);
        result.Value.Rounds.Select(round => round.CountryCode).Should().Equal([.. game.Rounds!.Select(round => round.StreakLocationCode!.ToUpperInvariant())]);
        result.Value.Rounds.Should().OnlyContain(round => round.GuessCountryCode == "KE" && round.Points != null && round.Panorama != null);
    }

    [Fact]
    public async Task AsksGeoNN_ForEachPanorama_AsTheGameShowedIt_AndTellsTheModelHowToUseIt()
    {
        _guesser.IsAvailable.Returns(true);
        _guesser.GuessAsync(Arg.Any<PanoramaView>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PanoramaGuess?>(new PanoramaGuess(21.9, 106.6, [new("VN", 0.94), new("CN", 0.03), new("LA", 0.01), new("TH", 0.01)], "v2")));
        var game = Game(GeoGuessrId, [Round(21.8, 106.7, "vn", PanoHex)], [Guess(23.5, 108.3, 412_400, 3870)]);
        game.ForbidRotating = true;
        game.Rounds![0].Heading = 225;
        game.Rounds[0].Pitch = -33.5;
        game.Rounds[0].Zoom = 1.9;
        Highscores(Token, game);

        var result = await Handler().Handle(new ReviewRoundsCommand(Discord, $"https://www.geoguessr.com/challenge/{Token}"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        await _guesser.Received(1).GuessAsync(
            new PanoramaView(PanoId, PanoramaViewMode.FixedView, 225, -33.5, 1.9), Arg.Any<CancellationToken>());
        await _imagery.Received(1).ReadViewsAsync(PanoId,
            Arg.Is<IReadOnlyList<StreetViewAngle>>(angles => angles.Count == 1 && angles[0].Heading == 225 && angles[0].Pitch == -33.5),
            Arg.Any<CancellationToken>());
        var reading = result.Value.Rounds.Single().Reading!;
        reading.Countries.Should().HaveCount(3, "CountriesShown");
        reading.DistanceKm.Should().BeApproximately(14.9, 0.5);
        result.Value.Answer.Notices.Should().Contain(notice => notice.Contains("GeoNN (v2)"));
        _asked.Single(request => !IsVision(request)).Messages[^1].ToPlainText()
            .Should().Contain("GeoNN read the panorama as Vietnam 94%, China 3%, Laos 1% (its guess 15 km off).")
            .And.Contain("never cite it");
    }

    [Fact]
    public async Task ReviewsWithoutGeoNN_WhenItIsNotSetUp_OrHasNoReading()
    {
        var link = $"https://www.geoguessr.com/challenge/{Token}";
        Highscores(Token, Game(GeoGuessrId, [Round(21.8, 106.7, "vn")], [Guess(23.5, 108.3, 412_400, 3870)]));
        var unset = await Handler().Handle(new ReviewRoundsCommand(Discord, link), CancellationToken.None);
        await _guesser.DidNotReceiveWithAnyArgs().GuessAsync(default!, default);

        _guesser.IsAvailable.Returns(true);
        _guesser.GuessAsync(Arg.Any<PanoramaView>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<PanoramaGuess?>(null));
        Highscores(Token, Game(GeoGuessrId, [new ChallengeResultRoundDto { Lat = 21.8, Lng = 106.7, PanoId = "abcdefghijklmnop" }], [Guess(23.5, 108.3, 412_400, 3870)]));
        var unanswered = await Handler().Handle(new ReviewRoundsCommand(Discord, link), CancellationToken.None);

        foreach (var result in new[] { unset, unanswered })
        {
            result.IsSuccess.Should().BeTrue();
            result.Value.Rounds.Single().Reading.Should().BeNull();
            result.Value.Answer.Notices.Should().NotContain(notice => notice.Contains("GeoNN"));
        }

        await _guesser.Received(1).GuessAsync(Arg.Is<PanoramaView>(view => view.PanoId == "abcdefghijklmnop" && view.Mode == PanoramaViewMode.FullPanorama), Arg.Any<CancellationToken>());
        _asked.Should().NotContain(request => request.Messages[request.Messages.Count - 1].ToPlainText().Contains("GeoNN"));
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
        var ai = Options.Create(_ai);
        var settings = Options.Create(_settings);
        var loop = new AiAnswerLoop(_chat, _catalog, _budget, toolbox, ai, tools, _clock, NullLogger<AiAnswerLoop>.Instance);
        var finder = new RoundClueFinder(_embedder, _index, countries, ai, settings, NullLogger<RoundClueFinder>.Instance);
        var sight = new ClueSightCheck(_imagery, _chat, _catalog, _budget, ai, settings, NullLogger<ClueSightCheck>.Instance);

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
            _users, _challenges, factory, _boundaries, _guesser, _mapsLinks, finder, sight, _relay, loop, _budget,
            geoGuessr, ai, settings, _clock, NullLogger<ReviewRoundsHandler>.Instance);
    }

    private static bool IsVision(AiChatRequest request) => request.Messages.Any(message => message.Parts.OfType<AiImagePart>().Any());

    private void Search(string country, params KnowledgeHit[] hits) =>
        _index.SearchAsync(Arg.Is<KnowledgeQuery>(query => query.Country == country), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<KnowledgeHit>>(hits));

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

    private static ChallengeResultRoundDto Round(double lat, double lng, string? code, string? panoId = null) =>
        new() { Lat = lat, Lng = lng, StreakLocationCode = code, PanoId = panoId };

    private static ChallengeResultGuessDto Guess(double? lat, double? lng, double? metres, int points, string? code = null) =>
        new() { Lat = lat, Lng = lng, DistanceInMeters = metres, RoundScoreInPoints = points, StreakLocationCode = code, SkippedRound = lat is null };

    private static KnowledgeHit Hit(string country, string heading, string text) =>
        new(Guid.NewGuid(), 0.5f, KnowledgeChunkKind.Text, text, $"https://docs.google.com/document/d/{country.Replace(' ', '-')}#heading=h.{heading}", null,
            "Region guide", country, $"Region guide > {heading}", null, 0, "gdoc", "x", heading);
}
