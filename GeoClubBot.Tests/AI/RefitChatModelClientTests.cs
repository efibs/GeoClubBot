using System.Net;
using System.Text;
using FluentAssertions;
using Infrastructure.OutputAdapters.AI.OpenRouter;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using UseCases.OutputPorts.AI;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// Parses a payload captured from the live OpenRouter models endpoint, trimmed to five representative
/// entries. Using the real wire shape matters here: the API is snake_case while the serializer defaults
/// to camelCase, so a missing <c>[JsonPropertyName]</c> silently yields nulls that look at runtime like
/// "no free models available" rather than like a bug.
/// </summary>
public sealed class RefitChatModelClientTests
{
    [Fact]
    public async Task ReadFreeModels_KeepsOnlyZeroCostModels()
    {
        var client = CreateClient(await ReadFixtureAsync("openrouter-models.json"));

        var result = await client.ReadFreeModelsAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(model => model.Id).Should().BeEquivalentTo(
            "liquid/lfm-2.5-2.6b:free",
            "dots-studio/dots-3-note-preview:free",
            "nvidia/nemotron-3.5-content-safety:free",
            "openrouter/free");
        result.Value.Should().NotContain(model => model.Id == "tencent/hy-mt2-1.8b", "that model is paid");
    }

    [Fact]
    public async Task ReadFreeModels_MapsTheFieldsTheSelectorDependsOn()
    {
        var client = CreateClient(await ReadFixtureAsync("openrouter-models.json"));

        var result = await client.ReadFreeModelsAsync();

        var vision = result.Value.Single(model => model.Id == "dots-studio/dots-3-note-preview:free");
        vision.SupportsImageInput.Should().BeTrue();
        vision.SupportsTools.Should().BeTrue();
        vision.ContextLength.Should().Be(512_000);
        vision.CreatedAt.Should().NotBeNull();

        // The API sends a bare date; it is read as end-of-day UTC so the model stays usable all day.
        vision.ExpiresAt.Should().NotBeNull();
        vision.ExpiresAt!.Value.UtcDateTime.Date.Should().Be(new DateTime(2026, 9, 30));

        var textOnly = result.Value.Single(model => model.Id == "liquid/lfm-2.5-2.6b:free");
        textOnly.SupportsImageInput.Should().BeFalse();
        textOnly.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task Complete_SendsTheWholeChain_AndReportsTheModelThatAnswered()
    {
        var handler = new CapturingHandler("""
            {
              "model": "nvidia/nemotron-3.5-content-safety:free",
              "choices": [{"message": {"role": "assistant", "content": "Bollards in Ghana are short and white."}}],
              "usage": {"prompt_tokens": 812, "completion_tokens": 44}
            }
            """);

        var result = await CreateClient(handler).CompleteAsync(new AiChatRequest(
            ["first/model", "second/model", "openrouter/free"],
            [AiChatMessage.User("What do Ghanaian bollards look like?")]));

        result.IsSuccess.Should().BeTrue();
        result.Value.Text.Should().Contain("Ghana");
        result.Value.ModelUsed.Should().Be("nvidia/nemotron-3.5-content-safety:free",
            "the provider may fall back, and the answering model is what we record");
        result.Value.Usage.PromptTokens.Should().Be(812);

        handler.LastRequestBody.Should().Contain("\"model\":\"first/model\"");
        handler.LastRequestBody.Should().Contain("\"models\":[\"first/model\",\"second/model\",\"openrouter/free\"]",
            "the fallback chain must reach the provider so it can fail over server-side");
    }

    [Fact]
    public async Task Complete_SendsImagesAsUrlParts_RatherThanInliningThem()
    {
        var handler = new CapturingHandler("""
            {"model":"m","choices":[{"message":{"content":"ok"}}],"usage":{"prompt_tokens":1,"completion_tokens":1}}
            """);

        await CreateClient(handler).CompleteAsync(new AiChatRequest(
            ["m"],
            [AiChatMessage.User("what is this?", ["https://i.imgur.com/abc.png"])]));

        handler.LastRequestBody.Should().Contain("\"type\":\"image_url\"");
        handler.LastRequestBody.Should().Contain("https://i.imgur.com/abc.png");
    }

    [Fact]
    public async Task Complete_SendsPlainStringContent_WhenATurnHasNoImages()
    {
        // Some providers are stricter than the OpenAI spec about array content on system turns.
        var handler = new CapturingHandler("""
            {"model":"m","choices":[{"message":{"content":"ok"}}],"usage":{}}
            """);

        await CreateClient(handler).CompleteAsync(new AiChatRequest(
            ["m"],
            [AiChatMessage.System("You are helpful."), AiChatMessage.User("hi")]));

        handler.LastRequestBody.Should().Contain("\"content\":\"You are helpful.\"");
        handler.LastRequestBody.Should().NotContain("\"type\":\"text\"");
    }

    [Fact]
    public async Task Complete_Fails_WhenTheProviderReportsAnErrorInBandWithHttp200()
    {
        var client = CreateClient(new CapturingHandler("""
            {"error": {"code": 502, "message": "upstream model unavailable"}}
            """));

        var result = await client.CompleteAsync(new AiChatRequest(["m"], [AiChatMessage.User("hi")]));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ai.chat_request_failed");
    }

    [Fact]
    public async Task Complete_ReportsRateLimiting_Distinctly()
    {
        // Reaching us means the whole chain was exhausted, which the caller surfaces as a budget
        // problem rather than a generic failure.
        var client = CreateClient(new CapturingHandler("{}", HttpStatusCode.TooManyRequests));

        var result = await client.CompleteAsync(new AiChatRequest(["m"], [AiChatMessage.User("hi")]));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ai.rate_limited");
    }

    [Fact]
    public async Task Complete_TrimsAnOverLongChain_ButKeepsTheFallbackRouter()
    {
        // OpenRouter refuses a request naming more than three models, and answers with a plain 400 —
        // so the limit is enforced here rather than trusted to whatever the selector was configured
        // with. The router is the entry that is always reachable, so it survives the trim.
        var handler = new CapturingHandler("""
            {"model":"m","choices":[{"message":{"content":"ok"}}],"usage":{"prompt_tokens":1,"completion_tokens":1}}
            """);

        await CreateClient(handler).CompleteAsync(new AiChatRequest(
            ["first/model", "second/model", "third/model", "fourth/model", "openrouter/free"],
            [AiChatMessage.User("hi")]));

        handler.LastRequestBody.Should()
            .Contain("\"models\":[\"first/model\",\"second/model\",\"openrouter/free\"]");
    }

    [Fact]
    public async Task Complete_OmitsTheFallbackArray_WhenOnlyOneModelIsOffered()
    {
        // A null "models" is not the same as an absent one to a provider validating the field.
        var handler = new CapturingHandler("""
            {"model":"m","choices":[{"message":{"content":"ok"}}],"usage":{"prompt_tokens":1,"completion_tokens":1}}
            """);

        await CreateClient(handler).CompleteAsync(new AiChatRequest(["only/model"], [AiChatMessage.User("hi")]));

        handler.LastRequestBody.Should().NotContain("\"models\"");
    }

    [Fact]
    public async Task Complete_Fails_WhenNoModelWasOffered()
    {
        var result = await CreateClient(new CapturingHandler("{}")).CompleteAsync(
            new AiChatRequest([], [AiChatMessage.User("hi")]));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ai.no_model_available");
    }

    private static async Task<CapturingHandler> ReadFixtureAsync(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "AI", name);
        return new CapturingHandler(await File.ReadAllTextAsync(path));
    }

    private static RefitChatModelClient CreateClient(CapturingHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://openrouter.ai") };

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(RefitChatModelClient.HttpClientName).Returns(httpClient);

        return new RefitChatModelClient(factory, NullLogger<RefitChatModelClient>.Instance);
    }

    /// <summary>Returns a canned body and records the request payload so the wire format can be asserted.</summary>
    private sealed class CapturingHandler(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public string LastRequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
                // HttpClient sets this itself; a hand-rolled handler must do it too, or Refit throws
                // while building its ApiException instead of surfacing the status code.
                RequestMessage = request
            };
        }
    }
}
