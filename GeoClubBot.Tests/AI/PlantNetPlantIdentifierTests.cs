using System.Net;
using System.Text;
using Configuration;
using FluentAssertions;
using Infrastructure.OutputAdapters.AI.PlantNet;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using UseCases.OutputPorts.AI;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// The Pl@ntNet adapter against canned responses in the shape of the API's documentation: the picture
/// goes as a file with the key in the query, the answer is read into candidates, and every way the
/// service says no comes back as a result, never a throw.
/// </summary>
public sealed class PlantNetPlantIdentifierTests
{
    private const string Screenshot = "https://cdn.discordapp.com/attachments/1/2/shot.png?ex=1&is=2&hm=3";

    private const string KesiyaAnswer = """
        {"query":{"project":"all","images":["shot.png"],"organs":["auto"],"includeRelatedImages":false},
         "language":"en","preferedReferential":"k-world-flora","bestMatch":"Pinus kesiya Royle ex Gordon",
         "results":[
          {"score":0.61234,"species":{"scientificNameWithoutAuthor":"Pinus kesiya","scientificNameAuthorship":"Royle ex Gordon",
            "genus":{"scientificNameWithoutAuthor":"Pinus"},"family":{"scientificNameWithoutAuthor":"Pinaceae"},
            "commonNames":["Khasi pine","Benguet pine"]},"gbif":{"id":"5285824"},"powo":{"id":"676598-1"}},
          {"score":0.2,"species":{"scientificNameWithoutAuthor":"Pinus roxburghii","scientificNameAuthorship":"Sarg.",
            "genus":{"scientificNameWithoutAuthor":"Pinus"},"family":{"scientificNameWithoutAuthor":"Pinaceae"},"commonNames":[]},"gbif":{"id":"5285750"}}
         ],"version":"2025-01-01","remainingIdentificationRequests":487}
        """;

    [Fact]
    public async Task PostsThePictureAsAFile_WithTheKeyInTheQuery_AndReadsTheCandidates()
    {
        var handler = new RoutingHandler()
            .OnImage(Screenshot, "image/png", [1, 2, 3, 4])
            .On("/v2/identify/all", KesiyaAnswer);

        var result = await CreateIdentifier(handler).IdentifyAsync(Screenshot);

        result.IsSuccess.Should().BeTrue();
        result.Value.RemainingRequests.Should().Be(487);
        result.Value.Candidates.Should().HaveCount(2);
        result.Value.Best.Should().BeEquivalentTo(new PlantCandidate(
            "Pinus kesiya", "Royle ex Gordon", "Pinus", "Pinaceae", ["Khasi pine", "Benguet pine"], 0.61234, 5285824));
        result.Value.Candidates[1].GbifTaxonKey.Should().Be(5285750);

        var post = handler.Posts.Should().ContainSingle().Subject;
        post.Url.Should().StartWith("https://plantnet.test/v2/identify/all?api-key=secret-key&lang=en&nb-results=3");
        post.ContentType.Should().StartWith("multipart/form-data");
        post.Body.Should().Contain("name=\"images\"; filename=\"screenshot.png\"").And.Contain("name=\"organs\"").And.Contain("auto");
        post.Body.Should().Contain("Content-Type: image/png");
    }

    [Fact]
    public async Task SaysNoPlantWasRecognised_OnTheServicesNotFound()
    {
        var handler = new RoutingHandler()
            .OnImage(Screenshot, "image/jpeg", [1])
            .On("/v2/identify/all", """{"statusCode":404,"error":"Not Found","message":"Species not found"}""", HttpStatusCode.NotFound);

        var result = await CreateIdentifier(handler).IdentifyAsync(Screenshot);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(PlantIdentificationErrorCodes.NotRecognised);
    }

    [Fact]
    public async Task ReportsTheServicesQuota_AndAsksNoMoreThatDay()
    {
        var handler = new RoutingHandler()
            .OnImage(Screenshot, "image/png", [1])
            .On("/v2/identify/all", """{"statusCode":429,"error":"Too Many Requests"}""", HttpStatusCode.TooManyRequests);
        var identifier = CreateIdentifier(handler);

        var first = await identifier.IdentifyAsync(Screenshot);
        var second = await identifier.IdentifyAsync("https://cdn.discordapp.com/attachments/1/3/other.png");

        first.Error.Code.Should().Be(PlantIdentificationErrorCodes.QuotaExceeded);
        second.Error.Code.Should().Be(PlantIdentificationErrorCodes.QuotaExceeded);
        handler.Posts.Should().ContainSingle("a 429 says the account is spent until midnight UTC");
    }

    [Fact]
    public async Task StopsAtItsOwnDailyCap_AndStartsAgainTheNextUtcDay()
    {
        var handler = new RoutingHandler().On("/v2/identify/all", KesiyaAnswer);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 12, 23, 50, 0, TimeSpan.Zero));
        var identifier = CreateIdentifier(handler, clock, maxPerDay: 2, cacheHours: 0);

        for (var i = 0; i < 3; i++)
        {
            handler.OnImage($"https://cdn.discordapp.com/attachments/1/{i}/shot.png", "image/png", [1]);
        }

        (await identifier.IdentifyAsync("https://cdn.discordapp.com/attachments/1/0/shot.png")).IsSuccess.Should().BeTrue();
        (await identifier.IdentifyAsync("https://cdn.discordapp.com/attachments/1/1/shot.png")).IsSuccess.Should().BeTrue();
        var third = await identifier.IdentifyAsync("https://cdn.discordapp.com/attachments/1/2/shot.png");

        third.Error.Code.Should().Be(PlantIdentificationErrorCodes.QuotaExceeded);
        handler.Posts.Should().HaveCount(2);

        clock.Advance(TimeSpan.FromMinutes(20));
        (await identifier.IdentifyAsync("https://cdn.discordapp.com/attachments/1/2/shot.png")).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AnswersTheSameScreenshotFromCache_WhateverItsSignature()
    {
        var handler = new RoutingHandler()
            .OnImage(Screenshot, "image/png", [1])
            .On("/v2/identify/all", KesiyaAnswer);
        var identifier = CreateIdentifier(handler);

        await identifier.IdentifyAsync(Screenshot);
        var again = await identifier.IdentifyAsync("https://cdn.discordapp.com/attachments/1/2/shot.png?ex=9&is=9&hm=9");

        again.IsSuccess.Should().BeTrue();
        handler.Posts.Should().ContainSingle("a follow-up on the same screenshot costs no identification");
    }

    [Fact]
    public async Task RefusesAPicture_InAFormatTheServiceDoesNotTake()
    {
        var handler = new RoutingHandler().OnImage(Screenshot, "image/webp", [1]).On("/v2/identify/all", KesiyaAnswer);

        var result = await CreateIdentifier(handler).IdentifyAsync(Screenshot);

        result.Error.Code.Should().Be(PlantIdentificationErrorCodes.ImageUnusable);
        result.Error.Type.Should().Be(ErrorType.Validation);
        handler.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task RefusesAPicture_OverTheSizeLimit()
    {
        var handler = new RoutingHandler().OnImage(Screenshot, "image/png", new byte[200_001]).On("/v2/identify/all", KesiyaAnswer);

        var result = await CreateIdentifier(handler, maxImageBytes: 200_000).IdentifyAsync(Screenshot);

        result.Error.Code.Should().Be(PlantIdentificationErrorCodes.ImageUnusable);
        handler.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task ReportsAnUnreachableService_WithoutThrowing_AndWithoutCachingIt()
    {
        var handler = new RoutingHandler { Failure = new HttpRequestException("down") };
        var identifier = CreateIdentifier(handler);

        var result = await identifier.IdentifyAsync(Screenshot);

        result.Error.Code.Should().Be(PlantIdentificationErrorCodes.ServiceFailed);
        result.Error.Type.Should().Be(ErrorType.Unexpected);

        handler.Failure = null;
        handler.OnImage(Screenshot, "image/png", [1]).On("/v2/identify/all", KesiyaAnswer);
        (await identifier.IdentifyAsync(Screenshot)).IsSuccess.Should().BeTrue("a failure to reach the service is no answer about the picture");
    }

    [Fact]
    public async Task IsUnavailable_WithoutAKey_AndSendsNothing()
    {
        var handler = new RoutingHandler().OnImage(Screenshot, "image/png", [1]).On("/v2/identify/all", KesiyaAnswer);
        var identifier = CreateIdentifier(handler, apiKey: "");

        identifier.IsAvailable.Should().BeFalse();
        (await identifier.IdentifyAsync(Screenshot)).Error.Code.Should().Be(PlantIdentificationErrorCodes.Unavailable);
        handler.Posts.Should().BeEmpty();
        handler.Gets.Should().BeEmpty();
    }

    private static PlantNetPlantIdentifier CreateIdentifier(
        RoutingHandler handler,
        TimeProvider? clock = null,
        int maxPerDay = 450,
        int cacheHours = 24,
        int maxImageBytes = 10 * 1024 * 1024,
        string apiKey = "secret-key")
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(PlantNetPlantIdentifier.HttpClientName).Returns(_ => new HttpClient(handler, disposeHandler: false));

        var tools = new AiToolsConfiguration
        {
            PlantIdentification =
            {
                ApiKey = apiKey,
                BaseUrl = "https://plantnet.test",
                MaxRequestsPerDay = maxPerDay,
                CacheHours = cacheHours,
                MaxImageBytes = maxImageBytes
            }
        };

        return new PlantNetPlantIdentifier(factory, Options.Create(tools), clock ?? TimeProvider.System, NullLogger<PlantNetPlantIdentifier>.Instance);
    }

    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (string Body, HttpStatusCode Status)> _routes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (string ContentType, byte[] Bytes)> _images = new(StringComparer.Ordinal);

        public List<(string Url, string ContentType, string Body)> Posts { get; } = [];

        public List<string> Gets { get; } = [];

        public Exception? Failure { get; set; }

        public RoutingHandler On(string path, string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _routes[path] = (body, status);
            return this;
        }

        public RoutingHandler OnImage(string url, string contentType, byte[] bytes)
        {
            _images[new Uri(url).GetLeftPart(UriPartial.Path)] = (contentType, bytes);
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            if (request.Method == HttpMethod.Get)
            {
                Gets.Add(request.RequestUri!.AbsoluteUri);
                if (_images.TryGetValue(request.RequestUri.GetLeftPart(UriPartial.Path), out var image))
                {
                    var content = new ByteArrayContent(image.Bytes);
                    content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(image.ContentType);
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request };
            }

            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Posts.Add((request.RequestUri!.AbsoluteUri, request.Content?.Headers.ContentType?.ToString() ?? string.Empty, body));

            var (answer, status) = _routes.TryGetValue(request.RequestUri.AbsolutePath, out var route)
                ? route
                : ("{}", HttpStatusCode.NotFound);

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(answer, Encoding.UTF8, "application/json"),
                RequestMessage = request
            };
        }
    }
}
