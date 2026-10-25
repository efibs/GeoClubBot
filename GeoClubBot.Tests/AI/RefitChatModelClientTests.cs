using System.Net;
using System.Text;
using System.Text.Json;
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
            "google/lyria-3-pro-preview",
            "openrouter/free");
        result.Value.Should().NotContain(model => model.Id == "tencent/hy-mt2-1.8b", "that model is paid");
    }

    [Fact]
    public async Task ReadFreeModels_MarksAGeneratorAsNotTextOnly()
    {
        // google/lyria-3-pro-preview is a real entry: a music generator billed per second of audio, so
        // both token prices read "0" and it passes the free filter. It stays in the roster — it is
        // free, and the descriptor's job is to report the provider honestly — but the flag is what
        // keeps the selector from ever offering it a chat completion.
        var client = CreateClient(await ReadFixtureAsync("openrouter-models.json"));

        var result = await client.ReadFreeModelsAsync();

        var generator = result.Value.Single(model => model.Id == "google/lyria-3-pro-preview");
        generator.ProducesTextOnly.Should().BeFalse("it emits audio as well as text");

        result.Value.Where(model => model.Id != "google/lyria-3-pro-preview")
            .Should().OnlyContain(model => model.ProducesTextOnly);
    }

    [Fact]
    public async Task ReadFreeModels_FlagsASafetyClassifier()
    {
        // nvidia/nemotron-3.5-content-safety:free is a real entry, and the reason this flag exists:
        // it is free, text-to-text, multimodal and 128k of context, so it ranks well and answers a
        // question with "User Safety: safe". Nothing in the structured roster separates it from an
        // assistant — the provider's own description is the only signal there is.
        var client = CreateClient(await ReadFixtureAsync("openrouter-models.json"));

        var result = await client.ReadFreeModelsAsync();

        var classifier = result.Value.Single(model => model.Id == "nvidia/nemotron-3.5-content-safety:free");
        classifier.IsGuardrail.Should().BeTrue();
        classifier.ProducesTextOnly.Should().BeTrue("it really does only emit text, which is the trap");

        result.Value.Where(model => model.Id != "nvidia/nemotron-3.5-content-safety:free")
            .Should().OnlyContain(model => !model.IsGuardrail,
                "a described assistant model must not be caught by the same rule");
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

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, ChatErrorCodes.Rejected)]
    [InlineData(HttpStatusCode.PaymentRequired, ChatErrorCodes.Rejected)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, ChatErrorCodes.Rejected)]
    [InlineData(HttpStatusCode.RequestTimeout, ChatErrorCodes.RequestFailed)]
    [InlineData(HttpStatusCode.BadGateway, ChatErrorCodes.RequestFailed)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ChatErrorCodes.RequestFailed)]
    public async Task Complete_TellsARequestRefusedAsSent_FromModelsThatFailed(HttpStatusCode status, string code)
    {
        // A replayed screenshot whose link expired fails the same way against every model. Reported as
        // a model failure, it would have the whole chain demoted and then retried against three more.
        var client = CreateClient(new CapturingHandler("{}", status));

        var result = await client.CompleteAsync(new AiChatRequest(["m"], [AiChatMessage.User("hi")]));

        result.Error.Code.Should().Be(code);
    }

    [Fact]
    public async Task Complete_ReportsATimeout_AsAFailure_RatherThanThrowing()
    {
        // Refit wraps a timeout in its own request exception, which this adapter did not catch, so an
        // answer that took too long escaped as an exception instead of the "could not answer" reply.
        //
        // Reported as unreachable rather than as a failed request: no model answered, so none may be
        // blamed for it, and retrying against other models would only add to the wait.
        var client = CreateClient(new UnreachableHandler(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.")));

        var result = await client.CompleteAsync(new AiChatRequest(["m"], [AiChatMessage.User("hi")]));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ChatErrorCodes.Unreachable);
    }

    [Fact]
    public async Task ReadFreeModels_ReportsAnUnreachableProvider_RatherThanThrowing()
    {
        var client = CreateClient(new UnreachableHandler(new HttpRequestException("Name or service not known")));

        var result = await client.ReadFreeModelsAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ai.model_roster_unavailable");
    }

    [Fact]
    public async Task Complete_TrimsAnOverLongChain_ToItsBestRankedHead()
    {
        // OpenRouter refuses a request naming more than three models, and answers with a plain 400 —
        // so the limit is enforced here rather than trusted to whatever the selector was configured
        // with. The chain is ordered best first and the router, when present, is its least preferred
        // entry: it once survived every trim, and it is what answered with a safety classifier.
        var handler = new CapturingHandler("""
            {"model":"m","choices":[{"message":{"content":"ok"}}],"usage":{"prompt_tokens":1,"completion_tokens":1}}
            """);

        await CreateClient(handler).CompleteAsync(new AiChatRequest(
            ["first/model", "second/model", "third/model", "fourth/model", "openrouter/free"],
            [AiChatMessage.User("hi")]));

        handler.LastRequestBody.Should()
            .Contain("\"models\":[\"first/model\",\"second/model\",\"third/model\"]");
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
    public async Task ReadFreeModels_ReadsEachModelsSize_FromItsIdOrItsProse()
    {
        var client = CreateClient(await ReadFixtureAsync("openrouter-models-20261023.json"));

        var models = (await client.ReadFreeModelsAsync()).Value.ToDictionary(model => model.Id);

        models["nvidia/nemotron-3-ultra-550b-a55b:free"].Should().Match<ChatModelDescriptor>(model =>
            model.TotalParametersBillions == 550 && model.ActiveParametersBillions == 55);
        // No size in its id: "with 16B active parameters out of 280B total".
        models["dots-studio/dots-3-note-preview:free"].EffectiveParametersBillions.Should().BeApproximately(66.9, 0.1);
        models["liquid/lfm-2.5-2.6b:free"].EffectiveParametersBillions.Should().Be(2.6);
        models["google/lyria-3-pro-preview"].EffectiveParametersBillions.Should().BeNull();
    }

    [Fact]
    public async Task ReadFreeModels_FlagsTheModelsBuiltForCodingAgents()
    {
        var client = CreateClient(await ReadFixtureAsync("openrouter-models-20261023.json"));

        var coding = (await client.ReadFreeModelsAsync()).Value
            .Where(model => model.IsCodingSpecialist)
            .Select(model => model.Id);

        // Liquid's blurb for LFM says Liquid "advises against using it for agentic coding", and Inkling's
        // lists coding among general uses: neither is a coding model.
        coding.Should().BeEquivalentTo(
            "recursive/flint-v1.0:free",
            "poolside/laguna-s-2.1:free",
            "poolside/laguna-xs-2.1:free",
            "cohere/north-mini-code:free");
    }

    [Fact]
    public async Task ReadModelHealth_ReportsTheBestEndpoint_FromTheFreeVariantsRoute()
    {
        var handler = new CapturingHandler("""
            {"data": {"id": "a/model:free", "endpoints": [
              {"provider_name": "Slow", "uptime_last_1d": 12.5},
              {"provider_name": "Fast", "uptime_last_1d": 97.25},
              {"provider_name": "New"}
            ]}}
            """);

        var result = await CreateClient(handler).ReadModelHealthAsync("a/model:free");

        result.Value.UptimeLastDayPercent.Should().Be(97.25, "the provider routes to whichever endpoint serves");
        handler.LastRequestUri!.AbsolutePath.Should().Be("/api/v1/models/a/model%3Afree/endpoints");
    }

    [Theory]
    [InlineData("""{"data": {"id": "a/model:free", "endpoints": [{"provider_name": "New"}]}}""")]
    [InlineData("""{"data": {"id": "a/model:free", "endpoints": []}}""")]
    public async Task ReadModelHealth_ReportsAModelWithoutAFigure_AsUnmeasured(string body) =>
        // Unmeasured is not down: the selector treats it as healthy.
        (await CreateClient(new CapturingHandler(body)).ReadModelHealthAsync("a/model:free"))
            .Value.UptimeLastDayPercent.Should().BeNull();

    [Fact]
    public async Task ReadModelHealth_GoesThroughTheHealthClient_NotTheChatPipeline()
    {
        // The chat pipeline's retry and circuit breaker would count a failing endpoints route against
        // every question; the health client shares only its rate limiter.
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(RefitChatModelClient.HealthHttpClientName).Returns(new HttpClient(
            new CapturingHandler("""{"data": {"id": "a/model:free", "endpoints": [{"uptime_last_1d": 99}]}}"""))
        {
            BaseAddress = new Uri("https://openrouter.ai")
        });
        var client = new RefitChatModelClient(factory, NullLogger<RefitChatModelClient>.Instance);

        (await client.ReadModelHealthAsync("a/model:free")).Value.UptimeLastDayPercent.Should().Be(99);
        factory.DidNotReceive().CreateClient(RefitChatModelClient.HttpClientName);
    }

    [Fact]
    public async Task ReadModelHealth_Fails_RatherThanThrowing_ForAModelTheProviderDoesNotKnow()
    {
        var client = CreateClient(new CapturingHandler("""{"error":{"message":"Not Found","code":404}}""", HttpStatusCode.NotFound));

        (await client.ReadModelHealthAsync("gone/model:free")).IsFailure.Should().BeTrue();
        (await client.ReadModelHealthAsync("no-author")).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Complete_ReportsAGatedHead_AsTheModelsFault_RatherThanARefusedRequest()
    {
        var client = CreateClient(new CapturingHandler(OpenRouterFailureClassifierTests.GatedInkling, HttpStatusCode.Forbidden));

        var result = await client.CompleteAsync(new AiChatRequest(
            ["thinkingmachines/inkling:free", "b/model"], [AiChatMessage.User("hi")]));

        result.Error.Code.Should().Be(ChatErrorCodes.ModelGated);
    }

    [Fact]
    public async Task Complete_ReportsAGateFurtherDownTheChain_AsAFailedRequest()
    {
        // The head failed as well — the provider passes on the head's error when it has one — so every
        // model failed, and only the head can be ruled out safely: never a model the error did not name.
        var client = CreateClient(new CapturingHandler(OpenRouterFailureClassifierTests.GatedInkling, HttpStatusCode.Forbidden));

        var result = await client.CompleteAsync(new AiChatRequest(
            ["a/model", "thinkingmachines/inkling:free"], [AiChatMessage.User("hi")]));

        result.Error.Code.Should().Be(ChatErrorCodes.RequestFailed);
    }

    [Fact]
    public async Task Complete_ReportsABusyHead_ApartFromOurOwnRateLimit()
    {
        // Measured: asked [gemma, inkling-small, inkling], OpenRouter answered with gemma's shared-pool 429.
        var client = CreateClient(new CapturingHandler(OpenRouterFailureClassifierTests.BusyGemma, HttpStatusCode.TooManyRequests));

        var result = await client.CompleteAsync(new AiChatRequest(
            ["google/gemma-4-31b-it:free", "b/model"], [AiChatMessage.User("hi")]));

        result.Error.Code.Should().Be(ChatErrorCodes.ModelRateLimited);
    }

    [Fact]
    public async Task Complete_ReportsABusyHead_ReportedInBand()
    {
        var client = CreateClient(new CapturingHandler("""
            {"error": {"code": 429, "message": "Provider returned error", "metadata": {"raw": "a/model is temporarily rate-limited upstream.", "limit_source": "upstream_provider_shared_pool"}}}
            """));

        var result = await client.CompleteAsync(new AiChatRequest(["a/model", "b/model"], [AiChatMessage.User("hi")]));

        result.Error.Code.Should().Be(ChatErrorCodes.ModelRateLimited);
    }

    [Fact]
    public async Task Complete_StillReportsAPlainForbidden_AsARefusedRequest()
    {
        var client = CreateClient(new CapturingHandler("""{"error": {"message": "Key limit exceeded", "code": 403}}""", HttpStatusCode.Forbidden));

        var result = await client.CompleteAsync(new AiChatRequest(["m"], [AiChatMessage.User("hi")]));

        result.Error.Code.Should().Be(ChatErrorCodes.Rejected);
    }

    [Fact]
    public async Task Complete_Fails_WhenNoModelWasOffered()
    {
        var result = await CreateClient(new CapturingHandler("{}")).CompleteAsync(
            new AiChatRequest([], [AiChatMessage.User("hi")]));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ai.no_model_available");
    }

    private static readonly AiToolDefinition SearchTool = new(
        "search_guides",
        "Search the guides again.",
        """{"type": "object", "properties": {"query": {"type": "string"}}, "required": ["query"]}""");

    [Fact]
    public async Task Complete_DeclaresTheTools_AndLetsTheModelChoose()
    {
        var handler = new CapturingHandler("""{"model":"m","choices":[{"message":{"content":"ok"}}]}""");

        await CreateClient(handler).CompleteAsync(new AiChatRequest(["m"], [AiChatMessage.User("hi")], Tools: [SearchTool]));

        using var body = JsonDocument.Parse(handler.LastRequestBody);
        var tool = body.RootElement.GetProperty("tools").EnumerateArray().Single();
        tool.GetProperty("type").GetString().Should().Be("function");
        tool.GetProperty("function").GetProperty("name").GetString().Should().Be("search_guides");
        tool.GetProperty("function").GetProperty("description").GetString().Should().Be("Search the guides again.");
        tool.GetProperty("function").GetProperty("parameters").GetProperty("required")[0].GetString()
            .Should().Be("query", "the schema goes out as JSON, not as a string of it");
        body.RootElement.GetProperty("tool_choice").GetString().Should().Be("auto");
    }

    [Fact]
    public async Task Complete_TellsTheModelToAnswer_WithTheToolsStillDeclared()
    {
        var handler = new CapturingHandler("""{"model":"m","choices":[{"message":{"content":"ok"}}]}""");

        await CreateClient(handler).CompleteAsync(new AiChatRequest(
            ["m"], [AiChatMessage.User("hi")], Tools: [SearchTool], ToolChoice: AiToolChoice.None));

        using var body = JsonDocument.Parse(handler.LastRequestBody);
        body.RootElement.GetProperty("tool_choice").GetString().Should().Be("none");
        body.RootElement.GetProperty("tools").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Complete_SendsNoToolFields_WhenNoToolIsDeclared()
    {
        var handler = new CapturingHandler("""{"model":"m","choices":[{"message":{"content":"ok"}}]}""");

        await CreateClient(handler).CompleteAsync(new AiChatRequest(["m"], [AiChatMessage.User("hi")], ToolChoice: AiToolChoice.None));

        handler.LastRequestBody.Should().NotContain("\"tools\"").And.NotContain("tool_choice").And.NotContain("tool_call");
    }

    [Fact]
    public async Task Complete_SendsAToolRoundBack_InTheShapeTheProviderExpects()
    {
        var handler = new CapturingHandler("""{"model":"m","choices":[{"message":{"content":"ok"}}]}""");

        await CreateClient(handler).CompleteAsync(new AiChatRequest(
            ["m"],
            [
                AiChatMessage.User("where are pole supports rare?"),
                AiChatMessage.AssistantToolCalls("\n\n\n", [new AiToolCall("call-1", "search_guides", """{"query":"pole supports"}""")]),
                AiChatMessage.ToolResult("call-1", "[3] Brazil > Poles\nNone in Ceará.")
            ],
            Tools: [SearchTool]));

        using var body = JsonDocument.Parse(handler.LastRequestBody);
        var messages = body.RootElement.GetProperty("messages");

        var assistant = messages[1];
        assistant.GetProperty("role").GetString().Should().Be("assistant");
        assistant.GetProperty("content").ValueKind.Should().Be(JsonValueKind.Null,
            "a turn that only called tools has null content, and whitespace is not text");
        var call = assistant.GetProperty("tool_calls").EnumerateArray().Single();
        call.GetProperty("id").GetString().Should().Be("call-1");
        call.GetProperty("type").GetString().Should().Be("function");
        call.GetProperty("function").GetProperty("name").GetString().Should().Be("search_guides");
        call.GetProperty("function").GetProperty("arguments").GetString()
            .Should().Be("""{"query":"pole supports"}""", "arguments travel as a string of JSON");

        var result = messages[2];
        result.GetProperty("role").GetString().Should().Be("tool");
        result.GetProperty("tool_call_id").GetString().Should().Be("call-1");
        result.GetProperty("content").GetString().Should().Be("[3] Brazil > Poles\nNone in Ceará.");
        messages[0].TryGetProperty("tool_calls", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Complete_ReadsAReplyThatOnlyCallsTools_AsASuccess()
    {
        // Captured from nemotron-3-ultra on 2026-10-23: no content at all, and the call. Read as an empty
        // response it blamed — and demoted — the very models that call tools properly.
        var handler = new CapturingHandler("""
            {"model":"nvidia/nemotron-3-ultra-550b-a55b:free","choices":[{"finish_reason":"tool_calls","message":{
              "role":"assistant","content":null,"tool_calls":[{"type":"function","index":0,"id":"call-0061f2b2",
              "function":{"name":"search_guides","arguments":"{\"country\":\"Brazil\",\"query\":\"pole supports rare Brazil\"}"}}]}}],
             "usage":{"prompt_tokens":438,"completion_tokens":76}}
            """);

        var result = await CreateClient(handler).CompleteAsync(new AiChatRequest(["m"], [AiChatMessage.User("hi")], Tools: [SearchTool]));

        result.IsSuccess.Should().BeTrue();
        result.Value.Text.Should().BeEmpty();
        result.Value.RequestsTools.Should().BeTrue();
        result.Value.FinishReason.Should().Be("tool_calls");
        result.Value.ToolCalls.Should().ContainSingle().Which.Should().Be(
            new AiToolCall("call-0061f2b2", "search_guides", """{"country":"Brazil","query":"pole supports rare Brazil"}"""));
    }

    [Fact]
    public async Task Complete_ReadsArguments_SentAsTheObjectItself()
    {
        var handler = new CapturingHandler("""
            {"model":"m","choices":[{"message":{"content":"\n\n\n","tool_calls":[
              {"id":"c1","function":{"name":"read_guide","arguments":{"excerpt":3}}},
              {"id":"c2","function":{"name":""}},
              {"function":{"name":"species_range"}}]}}]}
            """);

        var result = await CreateClient(handler).CompleteAsync(new AiChatRequest(["m"], [AiChatMessage.User("hi")], Tools: [SearchTool]));

        result.Value.ToolCalls.Should().Equal(
            new AiToolCall("c1", "read_guide", """{"excerpt":3}"""),
            // Nameless calls cannot be run or answered; one without an id or arguments keeps empty ones.
            new AiToolCall("", "species_range", "{}"));
    }

    [Fact]
    public async Task Complete_StillReportsAnEmptyReply_WhenItCallsNoTool()
    {
        var handler = new CapturingHandler("""{"model":"m","choices":[{"message":{"content":"  ","tool_calls":[]}}]}""");

        var result = await CreateClient(handler).CompleteAsync(new AiChatRequest(["m"], [AiChatMessage.User("hi")], Tools: [SearchTool]));

        result.Error.Code.Should().Be(ChatErrorCodes.EmptyResponse);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, """{"error":{"message":"No endpoints found that support tool use. To learn more about provider routing, visit: https://openrouter.ai/docs/provider-routing","code":404}}""")]
    [InlineData(HttpStatusCode.BadRequest, """{"error":{"message":"Provider returned error: tool_choice 'auto' is not supported","code":400}}""")]
    public async Task Complete_ReportsToolsTheProviderCannotUse_ApartFromARefusedRequest(HttpStatusCode status, string body)
    {
        var withTools = await CreateClient(new CapturingHandler(body, status))
            .CompleteAsync(new AiChatRequest(["m"], [AiChatMessage.User("hi")], Tools: [SearchTool]));
        var withoutTools = await CreateClient(new CapturingHandler(body, status))
            .CompleteAsync(new AiChatRequest(["m"], [AiChatMessage.User("hi")]));

        withTools.Error.Code.Should().Be(ChatErrorCodes.ToolsUnsupported, "the same question is worth asking without them");
        withoutTools.Error.Code.Should().Be(ChatErrorCodes.Rejected, "a request that declared no tool cannot have been refused for one");
    }

    private static async Task<CapturingHandler> ReadFixtureAsync(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "AI", name);
        return new CapturingHandler(await File.ReadAllTextAsync(path));
    }

    private static RefitChatModelClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://openrouter.ai") };

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(RefitChatModelClient.HttpClientName).Returns(httpClient);
        factory.CreateClient(RefitChatModelClient.HealthHttpClientName).Returns(httpClient);

        return new RefitChatModelClient(factory, NullLogger<RefitChatModelClient>.Instance);
    }

    /// <summary>Returns a canned body and records the request payload so the wire format can be asserted.</summary>
    private sealed class CapturingHandler(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public string LastRequestBody { get; private set; } = string.Empty;

        public Uri? LastRequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;

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

    private sealed class UnreachableHandler(Exception failure) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw failure;
    }
}
