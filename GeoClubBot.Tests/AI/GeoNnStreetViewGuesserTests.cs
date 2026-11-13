using System.Net;
using System.Text;
using Configuration;
using FluentAssertions;
using Infrastructure.OutputAdapters.AI.GeoNn;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.AI;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// Asking GeoNN's <c>play.py</c> about a panorama: the request it takes and the answer it gives, as read
/// from a live run on 2026-11-13; and that nothing it does fails the review.
/// </summary>
public sealed class GeoNnStreetViewGuesserTests
{
    private const string Answer = """
        {"model":"v2","lat":-1.1,"lng":36.9,"countries":[{"code":"tr","prob":0.004},{"code":"KE","prob":0.94}],
         "heatmap":[[-1.1,36.9,0.2]],"timings_ms":{"total":700.1},"mode":"nm"}
        """;

    [Fact]
    public async Task PostsThePanorama_AsTheGameShowedIt_AndReadsTheCountriesLikeliestFirst()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Answer);

        var guess = await Create(handler, model: "v2").GuessAsync(new PanoramaView("tph3y", PanoramaViewMode.FixedView, 225, -33.5, 1.9));

        guess.Should().BeEquivalentTo(new PanoramaGuess(-1.1, 36.9, [new("KE", 0.94), new("TR", 0.004)], "v2"));
        handler.Url.Should().Be("http://gpu-box:8765/api/predict");
        handler.Body.Should().Be("""{"pano_id":"tph3y","mode":"nmpz","heading":225,"pitch":-33.5,"zoom":1.9,"model":"v2"}""");

        await Create(handler).GuessAsync(new PanoramaView("tph3y", PanoramaViewMode.FullPanorama, 225, -33.5, 1.9));
        handler.Body.Should().Be("""{"pano_id":"tph3y","mode":"nm","pitch":0,"zoom":0}""", "the whole panorama has no view to name, and the newest model is GeoNN's to pick");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, """{"detail":"no panorama"}""")]
    [InlineData(HttpStatusCode.OK, "not json")]
    [InlineData(HttpStatusCode.OK, """{"model":"v2","countries":[]}""")]
    public async Task AnswersNothing_WhenGeoNNCannotSay(HttpStatusCode status, string body) =>
        (await Create(new StubHandler(status, body)).GuessAsync(new PanoramaView("x", PanoramaViewMode.FullPanorama))).Should().BeNull();

    [Fact]
    public async Task AsksNothing_WithoutAnAddress()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Answer);
        var guesser = Create(handler, baseUrl: "");

        guesser.IsAvailable.Should().BeFalse();
        (await guesser.GuessAsync(new PanoramaView("x", PanoramaViewMode.FullPanorama))).Should().BeNull();
        handler.Url.Should().BeNull();
    }

    [Fact]
    public async Task GivesUp_AtItsDeadline()
    {
        var guesser = Create(new StubHandler(HttpStatusCode.OK, Answer, TimeSpan.FromSeconds(30)), timeoutSeconds: 1);

        (await guesser.GuessAsync(new PanoramaView("x", PanoramaViewMode.FullPanorama))).Should().BeNull();
    }

    private static GeoNnStreetViewGuesser Create(StubHandler handler, string baseUrl = "http://gpu-box:8765/", string model = "", int timeoutSeconds = 30)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(GeoNnStreetViewGuesser.HttpClientName).Returns(_ => new HttpClient(handler, disposeHandler: false));
        var settings = new AiRoundReviewConfiguration { StreetViewGuesser = { BaseUrl = baseUrl, Model = model, TimeoutSeconds = timeoutSeconds } };
        return new GeoNnStreetViewGuesser(factory, Options.Create(settings), NullLogger<GeoNnStreetViewGuesser>.Instance);
    }

    private sealed class StubHandler(HttpStatusCode status, string body, TimeSpan? delay = null) : HttpMessageHandler
    {
        public string? Url { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri?.ToString();
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            if (delay is { } wait)
            {
                await Task.Delay(wait, cancellationToken);
            }

            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
