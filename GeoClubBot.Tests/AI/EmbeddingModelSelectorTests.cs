using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
/// #503: OpenRouter withdrew the free embedding model, and every question and ingestion run failed
/// until the configuration changed. The selector moves to a model that is served, and the embedder
/// moves with it when the model in use is withdrawn mid-run.
/// </summary>
public sealed class EmbeddingModelSelectorTests
{
    private const string Preferred = "nvidia/preferred:free";
    private const string Fallback = "nvidia/fallback:free";

    [Fact]
    public async Task Get_UsesTheConfiguredModel_WhenTheCatalogServesIt()
    {
        var provider = new FakeOpenRouter { Catalog = [Multimodal(Preferred), Multimodal(Fallback)] };

        var choice = await CreateSelector(provider).GetAsync();

        choice.ModelId.Should().Be(Preferred);
        choice.Dimensions.Should().Be(4);
        provider.EmbeddingRequests.Should().BeEmpty("a configured width needs no measuring");
    }

    [Fact]
    public async Task Get_MovesToTheFallback_AndLearnsItsWidth_WhenTheCatalogLeavesTheModelOut()
    {
        var provider = new FakeOpenRouter { Catalog = [Multimodal(Fallback)], Widths = { [Fallback] = 6 } };

        var choice = await CreateSelector(provider).GetAsync();

        choice.ModelId.Should().Be(Fallback);
        choice.Dimensions.Should().Be(6);
        choice.CollectionName.Should().Be(EmbeddingModelChoice.CollectionNameFor("knowledge", Fallback, 6));
        provider.EmbeddingRequests.Should().Equal(Fallback);
    }

    [Fact]
    public async Task Get_KeepsTheConfiguredModel_WhenTheCatalogCannotBeRead()
    {
        var provider = new FakeOpenRouter { CatalogStatus = HttpStatusCode.InternalServerError };

        var choice = await CreateSelector(provider).GetAsync();

        choice.ModelId.Should().Be(Preferred);
    }

    [Fact]
    public async Task Get_PicksAFreeTextAndImageModel_WhenNoConfiguredModelIsServed()
    {
        var provider = new FakeOpenRouter
        {
            Catalog =
            [
                TextOnly("nvidia/text-only:free"),
                Multimodal("paid/multimodal", price: "0.0000001"),
                Multimodal("nvidia/other-multimodal:free")
            ],
            Widths = { ["nvidia/other-multimodal:free"] = 8 }
        };

        var choice = await CreateSelector(provider).GetAsync();

        choice.ModelId.Should().Be("nvidia/other-multimodal:free",
            "a text-only model would leave every picture without its vector, and a paid one costs money");
        choice.Dimensions.Should().Be(8);
    }

    [Fact]
    public async Task Get_FallsBackToTheConfiguredModel_WhenNothingCanBePicked_AndAutoSelectIsOff()
    {
        var provider = new FakeOpenRouter { Catalog = [Multimodal("nvidia/other-multimodal:free")] };

        var choice = await CreateSelector(provider, autoSelect: false).GetAsync();

        choice.ModelId.Should().Be(Preferred, "its requests then fail with the provider's own reason");
        provider.EmbeddingRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Embed_SwitchesToTheFallback_AndRetries_WhenTheModelIsWithdrawnMidRun()
    {
        // What production logged from 2026-11-16: 404 "No endpoints found for …" on every request.
        var provider = new FakeOpenRouter
        {
            Catalog = [Multimodal(Preferred), Multimodal(Fallback)],
            Withdrawn = { Preferred },
            Widths = { [Fallback] = 6 }
        };
        var selector = CreateSelector(provider);
        var embedder = CreateEmbedder(provider, selector);

        var result = await embedder.EmbedAsync([new TextEmbeddingInput("bollards")]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.Length.Should().Be(6);
        embedder.ModelId.Should().Be(Fallback);
        embedder.Dimensions.Should().Be(6);
        selector.CollectionName.Should().Be(EmbeddingModelChoice.CollectionNameFor("knowledge", Fallback, 6));
    }

    [Fact]
    public async Task Embed_FailsWithThePublicCode_WhenTheModelIsWithdrawnAndNothingIsLeft()
    {
        var provider = new FakeOpenRouter { Catalog = [Multimodal(Preferred)], Withdrawn = { Preferred } };
        var embedder = CreateEmbedder(provider, CreateSelector(provider, fallbacks: [], autoSelect: false));

        var result = await embedder.EmbedAsync([new TextEmbeddingInput("bollards")]);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(EmbeddingErrorCodes.Failed);
        result.Error.Message.Should().Contain("No endpoints found");
    }

    [Fact]
    public async Task Embed_WithAFixedSelector_NeverReadsTheCatalog()
    {
        var provider = new FakeOpenRouter { Catalog = [Multimodal(Fallback)] };
        var embedder = new OpenRouterEmbedder(provider.Factory(), Options(provider, [], true), NullLogger<OpenRouterEmbedder>.Instance);

        var result = await embedder.EmbedAsync([new TextEmbeddingInput("bollards")]);

        result.IsSuccess.Should().BeTrue();
        provider.CatalogReads.Should().Be(0);
        embedder.ModelId.Should().Be(Preferred);
    }

    private static EmbeddingModelSelector CreateSelector(
        FakeOpenRouter provider,
        List<EmbeddingModelOption>? fallbacks = null,
        bool autoSelect = true) =>
        new(provider.Factory(),
            Options(provider, fallbacks ?? [new EmbeddingModelOption { ModelId = Fallback }], autoSelect),
            NullLogger<EmbeddingModelSelector>.Instance);

    private static OpenRouterEmbedder CreateEmbedder(FakeOpenRouter provider, EmbeddingModelSelector selector) =>
        new(provider.Factory(), Options(provider, [], true), selector, NullLogger<OpenRouterEmbedder>.Instance);

    private static IOptions<AiConfiguration> Options(
        FakeOpenRouter provider,
        List<EmbeddingModelOption> fallbacks,
        bool autoSelect)
    {
        provider.Widths.TryAdd(Preferred, 4);

        return Microsoft.Extensions.Options.Options.Create(new AiConfiguration
        {
            KnowledgeCollectionPrefix = "knowledge",
            OpenRouter = new OpenRouterConfiguration
            {
                EmbeddingModelId = Preferred,
                EmbeddingDimensions = 4,
                FallbackEmbeddingModels = fallbacks,
                AutoSelectEmbeddingModel = autoSelect
            }
        });
    }

    private static JsonObject Multimodal(string id, string price = "0") => Model(id, price, "text", "image");

    private static JsonObject TextOnly(string id) => Model(id, "0", "text");

    private static JsonObject Model(string id, string price, params string[] inputs) => new()
    {
        ["id"] = id,
        ["architecture"] = new JsonObject
        {
            ["input_modalities"] = new JsonArray([.. inputs.Select(input => (JsonNode)JsonValue.Create(input)!)]),
            ["output_modalities"] = new JsonArray("embeddings")
        },
        ["pricing"] = new JsonObject { ["prompt"] = price, ["completion"] = "0" }
    };

    /// <summary>OpenRouter's embedding catalog and embeddings endpoint, per model.</summary>
    private sealed class FakeOpenRouter : HttpMessageHandler
    {
        public List<JsonObject> Catalog { get; init; } = [];

        public HttpStatusCode CatalogStatus { get; init; } = HttpStatusCode.OK;

        public HashSet<string> Withdrawn { get; } = [];

        public Dictionary<string, int> Widths { get; } = [];

        public List<string> EmbeddingRequests { get; } = [];

        public int CatalogReads { get; private set; }

        public IHttpClientFactory Factory()
        {
            var factory = Substitute.For<IHttpClientFactory>();
            factory.CreateClient(RefitChatModelClient.HttpClientName)
                .Returns(_ => new HttpClient(this, disposeHandler: false) { BaseAddress = new Uri("https://openrouter.ai") });
            return factory;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/api/v1/embeddings/models")
            {
                CatalogReads++;
                return Respond(request, CatalogStatus, new JsonObject { ["data"] = new JsonArray([.. Catalog.Select(model => (JsonNode)model.DeepClone())]) });
            }

            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
            var model = body["model"]!.GetValue<string>();
            var inputs = body["input"]!.AsArray().Count;
            EmbeddingRequests.Add(model);

            if (Withdrawn.Contains(model))
            {
                return Respond(request, HttpStatusCode.NotFound, new JsonObject
                {
                    ["error"] = new JsonObject { ["message"] = $"No endpoints found for {model}.", ["code"] = 404 }
                });
            }

            var width = Widths.GetValueOrDefault(model, 4);
            return Respond(request, HttpStatusCode.OK, new JsonObject
            {
                ["data"] = new JsonArray([.. Enumerable.Range(0, inputs).Select(index => (JsonNode)new JsonObject
                {
                    ["index"] = index,
                    ["embedding"] = new JsonArray([.. Enumerable.Repeat(0.5f, width).Select(value => (JsonNode)JsonValue.Create(value))])
                })])
            });
        }

        private static HttpResponseMessage Respond(HttpRequestMessage request, HttpStatusCode status, JsonNode body) =>
            new(status)
            {
                Content = new StringContent(body.ToJsonString(JsonSerializerOptions.Default), Encoding.UTF8, "application/json"),
                RequestMessage = request
            };
    }
}
