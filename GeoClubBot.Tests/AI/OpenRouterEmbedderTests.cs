using System.Net;
using System.Text;
using System.Text.Json;
using Configuration;
using FluentAssertions;
using Infrastructure.OutputAdapters.AI.OpenRouter;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.AI;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// Covers the two ways an embedding adapter corrupts a vector index silently rather than loudly:
/// attaching vectors to the wrong inputs, and accepting vectors of the wrong width.
/// </summary>
public sealed class OpenRouterEmbedderTests
{
    private const int Dimensions = 8;

    [Fact]
    public async Task Embed_ReturnsOneVectorPerInput_InRequestOrder()
    {
        var handler = new QueueingHandler([EmbeddingBody([(0, 0.1f), (1, 0.2f), (2, 0.3f)])]);

        var result = await CreateEmbedder(handler).EmbedAsync(
        [
            new TextEmbeddingInput("first"),
            new TextEmbeddingInput("second"),
            new ImageEmbeddingInput("https://i.imgur.com/x.png")
        ]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);
        result.Value.Select(v => v.Span[0]).Should().Equal(0.1f, 0.2f, 0.3f);
    }

    [Fact]
    public async Task Embed_RealignsVectors_WhenTheProviderReturnsThemOutOfOrder()
    {
        // The provider is not required to preserve request order. Trusting arrival order would attach
        // each vector to the wrong chunk — retrieval would degrade with nothing obviously broken.
        var handler = new QueueingHandler([EmbeddingBody([(2, 0.3f), (0, 0.1f), (1, 0.2f)])]);

        var result = await CreateEmbedder(handler).EmbedAsync(
            [new TextEmbeddingInput("a"), new TextEmbeddingInput("b"), new TextEmbeddingInput("c")]);

        result.Value.Select(v => v.Span[0]).Should().Equal(0.1f, 0.2f, 0.3f);
    }

    [Fact]
    public async Task Embed_SplitsLargeSetsIntoBatches_AndConcatenatesInOrder()
    {
        // Batching is what makes indexing affordable, but it must not reorder across batch boundaries.
        var handler = new QueueingHandler(
        [
            EmbeddingBody([(0, 0.1f), (1, 0.2f)]),
            EmbeddingBody([(0, 0.3f), (1, 0.4f)]),
            EmbeddingBody([(0, 0.5f)])
        ]);

        var inputs = Enumerable.Range(0, 5).Select(i => (EmbeddingInput)new TextEmbeddingInput($"chunk {i}")).ToList();

        var result = await CreateEmbedder(handler, batchSize: 2).EmbedAsync(inputs);

        result.IsSuccess.Should().BeTrue();
        handler.RequestCount.Should().Be(3, "five inputs at a batch size of two is three requests");
        result.Value.Select(v => v.Span[0]).Should().Equal(0.1f, 0.2f, 0.3f, 0.4f, 0.5f);
    }

    [Fact]
    public async Task Embed_SendsTextAndImageInputsInTheirOwnShapes()
    {
        var handler = new QueueingHandler([EmbeddingBody([(0, 0.1f), (1, 0.2f)])]);

        await CreateEmbedder(handler).EmbedAsync(
            [new TextEmbeddingInput("bollards"), new ImageEmbeddingInput("https://i.imgur.com/x.png")]);

        handler.LastRequestBody.Should().Contain("\"type\":\"text\"");
        handler.LastRequestBody.Should().Contain("\"type\":\"image_url\"");
        handler.LastRequestBody.Should().Contain("https://i.imgur.com/x.png");
        handler.LastRequestBody.Should().Contain("\"encoding_format\":\"float\"");
    }

    [Fact]
    public async Task Embed_Fails_WhenTheModelReturnsUnexpectedDimensions()
    {
        // Guards against a silently swapped model: the vector store's schema is fixed at the
        // configured width, so a mismatch either errors on write or corrupts an existing collection.
        var handler = new QueueingHandler([EmbeddingBody([(0, 0.1f)], dimensions: Dimensions + 1)]);

        var result = await CreateEmbedder(handler).EmbedAsync([new TextEmbeddingInput("x")]);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ai.embedding_dimension_mismatch");
    }

    [Fact]
    public async Task Embed_Fails_WhenFewerVectorsComeBackThanWereRequested()
    {
        var handler = new QueueingHandler([EmbeddingBody([(0, 0.1f)])]);

        var result = await CreateEmbedder(handler).EmbedAsync(
            [new TextEmbeddingInput("a"), new TextEmbeddingInput("b")]);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ai.embedding_incomplete");
    }

    [Fact]
    public async Task Embed_SurfacesAnInBandProviderError()
    {
        // A single unfetchable image URL fails the whole batch this way, so it must not look like success.
        var handler = new QueueingHandler(["""
            {"error":{"code":400,"message":"Received 403 status code when fetching image from URL: https://example.invalid/x.png"}}
            """]);

        var result = await CreateEmbedder(handler).EmbedAsync([new ImageEmbeddingInput("https://example.invalid/x.png")]);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ai.embedding_failed");
    }

    [Fact]
    public async Task Embed_ShortCircuits_OnAnEmptyInputSet()
    {
        var handler = new QueueingHandler([]);

        var result = await CreateEmbedder(handler).EmbedAsync([]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        handler.RequestCount.Should().Be(0, "an empty set must not spend a request from the daily allowance");
    }

    /// <summary>Builds a response body whose vectors are constant-valued, so order is easy to assert.</summary>
    private static string EmbeddingBody((int Index, float Value)[] entries, int dimensions = Dimensions)
    {
        var data = entries.Select(entry => new
        {
            index = entry.Index,
            embedding = Enumerable.Repeat(entry.Value, dimensions).ToArray()
        });

        return JsonSerializer.Serialize(new { model = "test-embedder", data, usage = new { prompt_tokens = 10 } });
    }

    private static OpenRouterEmbedder CreateEmbedder(QueueingHandler handler, int batchSize = 32)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://openrouter.ai") };

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(RefitChatModelClient.HttpClientName).Returns(httpClient);

        var configuration = Options.Create(new AiConfiguration
        {
            OpenRouter = new OpenRouterConfiguration
            {
                EmbeddingModelId = "test-embedder",
                EmbeddingDimensions = Dimensions,
                EmbeddingBatchSize = batchSize
            }
        });

        return new OpenRouterEmbedder(factory, configuration, NullLogger<OpenRouterEmbedder>.Instance);
    }

    /// <summary>Serves queued bodies in order so batching can be observed across several requests.</summary>
    private sealed class QueueingHandler(IReadOnlyList<string> bodies) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        public string LastRequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            var body = bodies[Math.Min(RequestCount, bodies.Count - 1)];
            RequestCount++;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
                RequestMessage = request
            };
        }
    }
}
