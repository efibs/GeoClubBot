using System.Net;
using System.Text.Json;
using Configuration;
using FluentAssertions;
using Infrastructure.OutputAdapters.AI.StreetView;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Refit;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Retrieval;
using UseCases.UseCases.AI.Review;
using UseCases.UseCases.AI.StreetView;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// What #480 reads from outside: Street View's free thumbnails, Maps' short links, and a single game from
/// GeoGuessr; and a question's Street View link checked like a review's round.
/// </summary>
public sealed class StreetViewAdapterTests
{
    [Fact]
    public async Task Imagery_AsksForEachView_WithThePitchTurnedOver_AndKeepsOnlyRealPictures()
    {
        var real = new byte[GoogleStreetViewImagery.MinimumBytes + 1];
        var handler = new RecordingHandler(request => request.RequestUri!.Query.Contains("yaw=90", StringComparison.Ordinal)
            ? Picture(new byte[2_464])
            : request.RequestUri.Query.Contains("yaw=180", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.Forbidden)
                : Picture(real));
        var imagery = new GoogleStreetViewImagery(Clients(handler, GoogleStreetViewImagery.HttpClientName), NullLogger<GoogleStreetViewImagery>.Instance);

        var views = await imagery.ReadViewsAsync("3kZJIREjaW8A1P039qpRvA",
            [new StreetViewAngle(0, 2.16, 90), new StreetViewAngle(90, 0, 90), new StreetViewAngle(180, 0, 90), new StreetViewAngle(270, -10, 126.87)]);

        views.Select(view => view.Angle.Heading).Should().Equal(0, 270);
        views.Should().OnlyContain(view => view.ContentType == "image/jpeg" && view.Content.Length == real.Length);
        handler.Requests.Should().Contain(
            "https://streetviewpixels-pa.googleapis.com/v1/thumbnail?panoid=3kZJIREjaW8A1P039qpRvA&cb_client=maps_sv.tactile.gps&w=1024&h=576&yaw=0&pitch=-2.16&thumbfov=90");
        handler.Requests.Should().Contain(url => url.Contains("yaw=270&pitch=10&thumbfov=126.87", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MapsLinks_FollowsAShortLinkByHand_OnlyToGoogleMaps()
    {
        const string full = "https://www.google.com/maps/@34.0519954,-99.9644448,3a,90y,51.57h,92.16t/data=!3m7!1e1!3m5!1s3kZJIREjaW8A1P039qpRvA!2e0";
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/good" => Redirect(full),
            "/hop" => Redirect("https://maps.app.goo.gl/good"),
            "/away" => Redirect("https://example.com/phish"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var resolver = new GoogleMapsLinkResolver(Clients(handler, GoogleMapsLinkResolver.HttpClientName), NullLogger<GoogleMapsLinkResolver>.Instance);

        (await resolver.ResolveAsync("https://maps.app.goo.gl/good")).Should().Be(full);
        (await resolver.ResolveAsync("https://maps.app.goo.gl/hop")).Should().Be(full);
        (await resolver.ResolveAsync("https://maps.app.goo.gl/away")).Should().BeNull();
        (await resolver.ResolveAsync("https://maps.app.goo.gl/missing")).Should().BeNull();

        handler.Requests.Should().NotContain(url => url.StartsWith("https://example.com", StringComparison.Ordinal), "a hop away from Google is never taken");
        var asked = handler.Requests.Count;
        (await resolver.ResolveAsync("https://example.com/maps/anything")).Should().BeNull();
        handler.Requests.Should().HaveCount(asked, "only Google's shorteners are asked at all");
    }

    [Fact]
    public async Task GeoGuessrsGame_ReadsLikeAChallengesGame()
    {
        // GET /v3/games/{token} as GeoGuessr answered it on 2026-11-21 (the player's name and ids replaced):
        // the same shape as a game in a challenge's highscores, with its state and map besides.
        var json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "AI", "geoguessr-game-20261121.json"));

        var game = JsonSerializer.Deserialize<ChallengeResultGameDto>(json, SystemTextJsonContentSerializer.GetDefaultJsonSerializerOptions())!;

        game.Should().BeEquivalentTo(new { Token = "eOb5HPFMPcMjJ5oM", Type = "standard", State = "finished", MapName = "A Community World", ForbidMoving = true, IsFinished = true });
        game.Rounds.Should().HaveCount(5).And.OnlyContain(round => round.PanoId != null && round.StreakLocationCode != null && round.Heading != null);
        game.Player.Guesses.Should().HaveCount(5).And.OnlyContain(guess => guess.RoundScoreInPoints != null && guess.DistanceInMeters != null);
        GeoGuessrPanoId.Decode(game.Rounds![0].PanoId).Should().MatchRegex("^[A-Za-z0-9_-]{22}$");
    }

    [Fact]
    public async Task Question_WithAStreetViewLink_LeadsWithThePlaceAsChecked()
    {
        const string link = "https://www.google.com/maps/@21.8,106.7,3a,90y,10h,90t/data=!3m6!1e1!3m4!1stph3yE31gcf1MCnaufqVUg!2e0";
        var boundaries = Substitute.For<IRegionBoundaries>();
        boundaries.LocateAsync(21.8, 106.7, Arg.Any<CancellationToken>()).Returns(Task.FromResult<RegionPlace?>(new RegionPlace("VN", "Lạng Sơn")));
        var poles = new KnowledgeHit(Guid.NewGuid(), 0.5f, KnowledgeChunkKind.Text, "Concrete poles with a white band.", "https://example.org/vn", null,
            "Vietnam guide", "vietnam", "Vietnam guide > Poles", null, 0);
        var index = Substitute.For<IKnowledgeIndex>();
        index.ListCountriesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<string>>(["vietnam"]));
        index.SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<KnowledgeHit>>([poles]));
        var embedder = Substitute.For<IEmbedder>();
        embedder.EmbedAsync(Arg.Any<IReadOnlyList<EmbeddingInput>>(), Arg.Any<CancellationToken>())
            .Returns(call => Result<IReadOnlyList<ReadOnlyMemory<float>>>.Success([.. ((IReadOnlyList<EmbeddingInput>)call[0]).Select(_ => (ReadOnlyMemory<float>)new float[] { 1 })]));
        var imagery = Substitute.For<IStreetViewImagery>();
        imagery.ReadViewsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<StreetViewAngle>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<StreetViewPicture>>([.. ((IReadOnlyList<StreetViewAngle>)call[1]).Select(angle => new StreetViewPicture([1], "image/jpeg", angle))]));
        var chat = Substitute.For<IChatModelClient>();
        chat.CompleteAsync(Arg.Any<AiChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<AiChatResponse>.Success(new AiChatResponse("1: yes — picture 3, left: a pole with a white band\nBest picture: 3", "vision/model", new AiTokenUsage(1, 1))));
        var catalog = Substitute.For<IChatModelCatalog>();
        catalog.ReadChainAsync(Arg.Any<ChatModelRequirements>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<string>>(["vision/model"]));
        var budget = Substitute.For<IAiBudgetRepository>();
        budget.TryReserveRequestsAsync(Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));
        var relay = Substitute.For<IImageRelay>();
        relay.IsEnabled.Returns(true);
        relay.StoreAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result<string>.Success("https://bot.example/api/v1/ai/images/v.jpg")));
        var ai = Options.Create(new AiConfiguration { Active = true, OpenRouter = { DailyRequestBudget = 100 } });
        var settings = Options.Create(new AiRoundReviewConfiguration());
        var countries = new KnowledgeCountryDirectory(index, Options.Create(new AiRetrievalConfiguration()), TimeProvider.System, NullLogger<KnowledgeCountryDirectory>.Instance);
        var lookAhead = new StreetViewLinkLookAhead(
            Substitute.For<IMapsLinkResolver>(), boundaries,
            new RoundClueFinder(embedder, index, countries, ai, settings, NullLogger<RoundClueFinder>.Instance),
            new ClueSightCheck(imagery, chat, catalog, budget, ai, settings, NullLogger<ClueSightCheck>.Instance),
            relay, budget, ai, settings, NullLogger<StreetViewLinkLookAhead>.Instance);

        var offer = await lookAhead.ReadAsync($"what gives this away? {link}", withPicture: false, new DateOnly(2026, 11, 21), CancellationToken.None);

        offer!.Hits.Should().HaveCount(2);
        var place = offer.Hits[0];
        place.Should().BeEquivalentTo(new { Kind = KnowledgeChunkKind.Image, ImageUrl = "https://bot.example/api/v1/ai/images/v.jpg", Title = StreetViewLinkLookAhead.SourceTitle });
        place.SourceUrl.Should().StartWith("https://www.google.com/maps/@?api=1&map_action=pano&pano=tph3yE31gcf1MCnaufqVUg");
        place.Text.Should().Be(
            "The member's Google Maps link is a Street View panorama in Vietnam (Lạng Sơn). A vision model checked its views against the guides' clues for the place. "
            + "It saw \"Vietnam guide > Poles\" (picture 3, left: a pole with a white band). Explain what gives the place away from the clues it was seen to show, "
            + "the country's first, citing their excerpts. Never say it shows a clue it was not seen to show. Cite this excerpt to show the place.");
        offer.Hits[1].Should().Be(poles);
        offer.Notices.Should().Equal(StreetViewLinkLookAhead.CheckedNotice);

        (await lookAhead.ReadAsync("how do I tell Laos from Vietnam?", withPicture: false, new DateOnly(2026, 11, 21), CancellationToken.None)).Should().BeNull();
        (await lookAhead.ReadAsync($"what tree is this? {link}", withPicture: true, new DateOnly(2026, 11, 21), CancellationToken.None))
            .Should().BeNull("beside a screenshot, a Maps link says where it was taken");
        (await lookAhead.ReadAsync("https://www.google.com/maps/@21.8,106.7,15z", withPicture: false, new DateOnly(2026, 11, 21), CancellationToken.None))!
            .Should().BeEquivalentTo(new { Hits = Array.Empty<KnowledgeHit>() }, "a map is no panorama: only a notice says why");
    }

    private static HttpResponseMessage Picture(byte[] bytes) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) { Headers = { ContentType = new("image/jpeg") } } };

    private static HttpResponseMessage Redirect(string location) =>
        new(HttpStatusCode.Found) { Headers = { Location = new Uri(location) } };

    private static IHttpClientFactory Clients(HttpMessageHandler handler, string name)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(name).Returns(_ => new HttpClient(handler, disposeHandler: false));
        return factory;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        private readonly List<string> _requests = [];

        public IReadOnlyList<string> Requests
        {
            get
            {
                lock (_requests)
                {
                    return [.. _requests];
                }
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (_requests)
            {
                _requests.Add(request.RequestUri!.ToString());
            }

            return Task.FromResult(answer(request));
        }
    }
}
