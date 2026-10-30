using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Infrastructure.OutputAdapters.AI.OpenRouter.Dtos;
using Microsoft.Extensions.Logging;
using Refit;
using UseCases.OutputPorts.AI;
using Utilities;

namespace Infrastructure.OutputAdapters.AI.OpenRouter;

/// <summary>
/// Adapts the OpenRouter wire format to the vendor-neutral <see cref="IChatModelClient"/> port, so no
/// OpenRouter type escapes the infrastructure layer.
///
/// Builds the Refit proxy per call from <see cref="IHttpClientFactory"/> rather than taking one by
/// injection, mirroring <c>GeoGuessrClientFactory</c>: this type is a singleton, and capturing a
/// single HttpClient for the process lifetime would pin one handler forever and defeat rotation.
/// Refit caches the generated implementation type, so the per-call proxy is just an allocation.
/// </summary>
public partial class RefitChatModelClient(
    IHttpClientFactory httpClientFactory,
    ILogger<RefitChatModelClient> logger) : IChatModelClient
{
    /// <summary>Name of the configured HttpClient carrying the base address, auth header and resilience pipeline.</summary>
    public const string HttpClientName = "OpenRouter";

    /// <summary>
    /// Name of the HttpClient the provider health reads go through: the same address and the same rate
    /// limiter as <see cref="HttpClientName"/>, but neither its retry nor its circuit breaker. A refresh
    /// reads up to twenty models in a row, and through the chat pipeline an endpoints route failing on its
    /// own — while chat answered — could open the breaker by itself and fail every question for two
    /// minutes. A failed read only leaves a model unmeasured, so it is not worth a retry either.
    /// </summary>
    public const string HealthHttpClientName = "OpenRouterHealth";

    /// <summary>
    /// Most models one request may name. OpenRouter refuses a longer list outright with
    /// <c>'models' array must have 3 items or fewer</c>, so it is enforced here rather than trusted to
    /// configuration: a wire limit belongs with the wire format. The catalog clamps its chains to this
    /// too, so the models a caller believes it asked are the ones that actually went out.
    /// </summary>
    internal const int MaxModelsPerRequest = 3;

    /// <summary>Response bodies are logged for diagnosis, so cap what a failing provider can write to the log.</summary>
    private const int MaxLoggedBodyLength = 500;

    private IOpenRouterApi CreateApi(string clientName = HttpClientName) =>
        RestService.For<IOpenRouterApi>(httpClientFactory.CreateClient(clientName));

    public async Task<Result<IReadOnlyList<ChatModelDescriptor>>> ReadFreeModelsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await CreateApi().ReadModelsAsync(cancellationToken).ConfigureAwait(false);

            var free = response.Data
                .Where(IsFree)
                .Select(ToDescriptor)
                .ToList();

            LogRosterRead(logger, free.Count, response.Data.Count);

            return free;
        }
        catch (ApiException ex)
        {
            LogRosterFailed(logger, (int)ex.StatusCode, Describe(ex), ex);
            return Error.Unexpected("ai.model_roster_unavailable",
                $"Could not read the model roster from the AI provider (HTTP {(int)ex.StatusCode}).");
        }
        catch (ApiRequestException ex)
        {
            // Refit's wrapper for every failure to get a response, timeouts included.
            LogRosterFailed(logger, 0, ex.InnerException?.Message ?? ex.Message, ex);
            return Error.Unexpected("ai.model_roster_unavailable",
                "Could not reach the AI provider to read the model roster.");
        }
    }

    public async Task<Result<AiChatResponse>> CompleteAsync(
        AiChatRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ModelChain.Count == 0)
        {
            return Error.Unexpected(ChatErrorCodes.NoModelAvailable, "No AI model is currently available.");
        }

        var declaresTools = request.Tools is { Count: > 0 };
        var payload = new OpenRouterChatRequestDto
        {
            Model = request.ModelChain[0],
            // Only send the fallback array when there is something to fall back to.
            Models = request.ModelChain.Count > 1 ? BuildFallbackChain(request.ModelChain) : null,
            Messages = [.. request.Messages.Select(ToMessageDto)],
            Temperature = request.Temperature,
            MaxTokens = request.MaxTokens,
            Tools = declaresTools ? [.. request.Tools!.Select(ToToolDto)] : null,
            ToolChoice = declaresTools ? (request.ToolChoice == AiToolChoice.None ? "none" : "auto") : null
        };

        try
        {
            var response = await CreateApi().CreateChatCompletionAsync(payload, cancellationToken).ConfigureAwait(false);

            // OpenRouter reports some upstream failures in-band with HTTP 200, so a successful
            // response object is not by itself a successful completion.
            if (response.Error is { } error)
            {
                LogCompletionRejected(logger, error.Code ?? 0, error.Message ?? "(no message)");

                if (declaresTools && MentionsTools(error.Message))
                {
                    return ToolsUnsupported();
                }

                var inBand = OpenRouterFailureClassifier.Classify(error.Message, error.Metadata);
                return ToModelFailure(inBand, request.ModelChain)
                       ?? Error.Unexpected(ChatErrorCodes.RequestFailed,
                           "The AI provider rejected the request. Please try again in a moment.");
            }

            var choice = response.Choices?.FirstOrDefault();
            var text = choice?.Message?.Content;
            var toolCalls = ReadToolCalls(choice?.Message?.ToolCalls);

            // A reply that only calls tools has no content, and is an answer all the same. Read as empty
            // it blamed the model, and demoted the very models that call tools properly.
            if (toolCalls.Count == 0 && string.IsNullOrWhiteSpace(text))
            {
                LogCompletionEmpty(logger, response.Model ?? "(unknown)");
                return Error.Unexpected(ChatErrorCodes.EmptyResponse, "The AI model returned an empty response.");
            }

            return new AiChatResponse(
                text ?? string.Empty,
                response.Model ?? request.ModelChain[0],
                new AiTokenUsage(response.Usage?.PromptTokens ?? 0, response.Usage?.CompletionTokens ?? 0),
                toolCalls.Count > 0 ? toolCalls : null,
                choice?.FinishReason);
        }
        catch (ApiException ex)
        {
            LogCompletionFailed(logger, (int)ex.StatusCode, Describe(ex), ex);

            var status = (int)ex.StatusCode;

            // "No endpoints found that support tool use": the tools are refused, not the question, which
            // is worth asking again without them. Told apart by the body, since the status says neither.
            if (declaresTools && status is >= 400 and < 500 and not 408 and not 429 && MentionsTools(ex.Content))
            {
                return ToolsUnsupported();
            }
            if (status is 403 or 429)
            {
                // The status code alone cannot tell a refused request or our own exhausted allowance from
                // one model's trouble; the body can. See OpenRouterFailureClassifier.
                var failure = OpenRouterFailureClassifier.Classify(ex.Content);
                if (ToModelFailure(failure, request.ModelChain) is { } modelFailure)
                {
                    return modelFailure;
                }

                if (failure.Kind != OpenRouterFailureKind.Other)
                {
                    // One model's trouble, but not provably the head's: the provider passed on the error of
                    // a model further down the chain, so the head failed as well, for a reason it did not
                    // give — or named no model, and a gate is too lasting a verdict to pin on a guess.
                    // Every model failed, which is what a failed request means, and others may still answer.
                    LogModelFailureNotAtHead(logger, failure.NamedModelId ?? "(none)", failure.Kind.ToString());
                    return Error.Unexpected(ChatErrorCodes.RequestFailed,
                        "The AI provider could not answer that right now.");
                }
            }

            return status switch
            {
                // A 429 against our own key survives the whole chain only when every model is exhausted,
                // so it is worth its own message: the caller surfaces it as a budget problem rather than a
                // generic failure.
                429 => Error.Conflict(ChatErrorCodes.RateLimited,
                    "The AI provider is rate-limiting us right now. Please try again shortly."),
                // Any other client error is about the request itself — an expired screenshot link, a body
                // too large, the key — and would fail the same way against any model. Kept apart from a
                // model failure so the chain is neither blamed for it nor retried with others.
                >= 400 and < 500 and not 408 => Error.Unexpected(ChatErrorCodes.Rejected,
                    "The AI provider could not answer that right now."),
                _ => Error.Unexpected(ChatErrorCodes.RequestFailed,
                    "The AI provider could not answer that right now.")
            };
        }
        catch (ApiRequestException ex)
        {
            // Refit's wrapper for every failure to get a response, timeouts included. Catching the inner
            // types directly let a timed-out answer escape instead of reporting it.
            //
            // Kept distinct from a failed request: nothing here says anything about the models named, so
            // the caller must neither blame them nor spend a retry on others while the provider is out.
            LogCompletionFailed(logger, 0, ex.InnerException?.Message ?? ex.Message, ex);
            return Error.Unexpected(ChatErrorCodes.Unreachable, "Could not reach the AI provider.");
        }
    }

    public async Task<Result<ChatModelHealth>> ReadModelHealthAsync(
        string modelId,
        CancellationToken cancellationToken = default)
    {
        var slash = modelId.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0 || slash == modelId.Length - 1)
        {
            // The endpoints route needs an author and a slug; an id without both cannot be looked up.
            return Error.Validation("ai.model_health_unavailable", $"'{modelId}' is not an author/slug model id.");
        }

        try
        {
            var response = await CreateApi(HealthHttpClientName)
                .ReadModelEndpointsAsync(modelId[..slash], modelId[(slash + 1)..], cancellationToken)
                .ConfigureAwait(false);

            // The best endpoint, because the provider routes to whichever serves: one healthy endpoint is
            // enough for the model to answer. An endpoint without a figure has had no traffic to measure,
            // and a model none of whose endpoints has one is unmeasured rather than down.
            var uptime = response.Data?.Endpoints?
                .Select(endpoint => endpoint.UptimeLastDay)
                .Where(value => value is not null)
                .Max();

            LogHealthRead(logger, modelId, uptime);

            return new ChatModelHealth(uptime);
        }
        catch (ApiException ex)
        {
            LogHealthFailed(logger, modelId, (int)ex.StatusCode, Describe(ex));
            return Error.Unexpected("ai.model_health_unavailable",
                $"Could not read the provider health of {modelId} (HTTP {(int)ex.StatusCode}).");
        }
        catch (ApiRequestException ex)
        {
            LogHealthFailed(logger, modelId, 0, ex.InnerException?.Message ?? ex.Message);
            return Error.Unexpected("ai.model_health_unavailable",
                $"Could not reach the AI provider to read the provider health of {modelId}.");
        }
    }

    /// <summary>
    /// The error for a failure that is one model's doing, or null when it is not — or when the model the
    /// provider names is not the chain's head. The head is the model sent as <c>model</c>, and the one
    /// whose error the provider passes on: asked <c>[gemma, inkling-small, inkling]</c> on 2026-10-23, it
    /// answered with gemma's 429 although both models after it refuse this bot as well.
    /// </summary>
    private static Error? ToModelFailure(OpenRouterFailure failure, IReadOnlyList<string> chain) => failure.Kind switch
    {
        // Excluded until restart, so only ever for the model the provider named.
        OpenRouterFailureKind.AgenticHarnessGate when failure.NamesHead(chain) => Error.Unexpected(
            ChatErrorCodes.ModelGated,
            "The AI model I tried doesn't serve this bot. Please try again in a moment."),
        // Only a passing demotion, so a message that names no model is still taken to mean the head.
        OpenRouterFailureKind.UpstreamRateLimit when failure.NamedModelId is null || failure.NamesHead(chain) =>
            Error.Unexpected(
                ChatErrorCodes.ModelRateLimited,
                "The AI model I tried is busy right now. Please try again shortly."),
        _ => null
    };

    /// <summary>
    /// Trims an over-long chain to what the provider accepts, keeping its head. The chain is ordered
    /// best first, and the fallback router — when it appears at all — is its least preferred entry, so
    /// the head is exactly what should survive.
    /// </summary>
    private static List<string> BuildFallbackChain(IReadOnlyList<string> chain) =>
        [.. chain.Take(MaxModelsPerRequest)];

    private static Error ToolsUnsupported() =>
        Error.Unexpected(ChatErrorCodes.ToolsUnsupported, "The AI provider could not use tools for this request.");

    /// <summary>Whether a refusal's text is about the tools: "support tool use", "tool_choice", "tools".</summary>
    private static bool MentionsTools(string? body) => body is not null && ToolTerms().IsMatch(body);

    private static OpenRouterToolDto ToToolDto(AiToolDefinition tool)
    {
        using var schema = JsonDocument.Parse(tool.ParametersSchemaJson);
        return new OpenRouterToolDto
        {
            Function = new OpenRouterFunctionDto
            {
                Name = tool.Name,
                Description = tool.Description,
                Parameters = schema.RootElement.Clone()
            }
        };
    }

    /// <summary>
    /// The calls a reply asked for, in its order. A call without a name cannot be run or answered, so it
    /// is dropped; one without an id keeps an empty one, which the caller fills in, since its result has
    /// to name it. Arguments arrive as a string of JSON, or from some providers as the object itself.
    /// </summary>
    private static List<AiToolCall> ReadToolCalls(List<OpenRouterToolCallDto>? calls) =>
    [
        .. (calls ?? [])
            .Where(call => !string.IsNullOrWhiteSpace(call.Function?.Name))
            .Select(call => new AiToolCall(
                call.Id ?? string.Empty,
                call.Function!.Name!,
                call.Function.Arguments is not { } arguments
                    ? "{}"
                    : arguments.ValueKind == JsonValueKind.String
                        ? arguments.GetString() ?? "{}"
                        : arguments.GetRawText()))
    ];

    private static OpenRouterMessageDto ToMessageDto(AiChatMessage message)
    {
        if (message.Role == AiChatRole.Tool)
        {
            var result = message.Parts.OfType<AiToolResultPart>().FirstOrDefault()
                         ?? throw new NotSupportedException("A tool message must carry the result it answers with.");
            return new OpenRouterMessageDto { Role = "tool", Content = result.Content, ToolCallId = result.ToolCallId };
        }

        var role = message.Role switch
        {
            AiChatRole.System => "system",
            AiChatRole.Assistant => "assistant",
            _ => "user"
        };

        var calls = message.ToolCalls.ToList();
        if (calls.Count > 0)
        {
            // An assistant turn that called tools goes back as it came: its calls, and its text or null.
            var text = string.Join(" ", message.Parts.OfType<AiTextPart>().Select(part => part.Text));
            return new OpenRouterMessageDto
            {
                Role = role,
                Content = string.IsNullOrWhiteSpace(text) ? null : text,
                ToolCalls =
                [
                    .. calls.Select(call => new OpenRouterToolCallDto
                    {
                        Id = call.Id,
                        Function = new OpenRouterFunctionCallDto
                        {
                            Name = call.Name,
                            Arguments = JsonSerializer.SerializeToElement(
                                string.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson)
                        }
                    })
                ]
            };
        }

        var hasImages = message.Parts.OfType<AiImagePart>().Any();
        if (!hasImages)
        {
            // Plain-string content for text-only turns: broader provider compatibility than an array.
            return new OpenRouterMessageDto { Role = role, Content = message.ToPlainText() };
        }

        var parts = message.Parts
            .Select(part => part switch
            {
                AiTextPart text => new OpenRouterContentPartDto { Type = "text", Text = text.Text },
                AiImagePart image => new OpenRouterContentPartDto
                {
                    Type = "image_url",
                    ImageUrl = new OpenRouterImageUrlDto { Url = image.Url }
                },
                _ => throw new NotSupportedException($"Unsupported content part '{part.GetType().Name}'.")
            })
            .ToList();

        return new OpenRouterMessageDto { Role = role, Content = parts };
    }

    /// <summary>Free means both prompt and completion cost exactly zero — not merely "cheap".</summary>
    private static bool IsFree(OpenRouterModelDto model) =>
        IsZeroPrice(model.Pricing?.Prompt) && IsZeroPrice(model.Pricing?.Completion);

    private static bool IsZeroPrice(string? price) =>
        decimal.TryParse(price, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value == 0m;

    private static ChatModelDescriptor ToDescriptor(OpenRouterModelDto model)
    {
        var modalities = model.Architecture?.InputModalities ?? [];
        var outputs = model.Architecture?.OutputModalities ?? [];
        var parameters = model.SupportedParameters ?? [];

        var size = ModelParameterCountParser.Parse(model.Id, model.Name, model.Description);

        return new ChatModelDescriptor(
            model.Id,
            model.Name ?? model.Id,
            model.ContextLength ?? model.TopProvider?.ContextLength ?? 0,
            model.TopProvider?.MaxCompletionTokens,
            modalities.Contains("image", StringComparer.OrdinalIgnoreCase),
            // Unstated output modalities are read as text-only rather than as a disqualification: a
            // provider that stops sending the field must not silently empty the whole roster.
            outputs.Count == 0 || outputs.All(output => output.Equals("text", StringComparison.OrdinalIgnoreCase)),
            parameters.Contains("tools", StringComparer.OrdinalIgnoreCase),
            parameters.Contains("structured_outputs", StringComparer.OrdinalIgnoreCase),
            IsGuardrail(model.Description),
            model.Created is { } created ? DateTimeOffset.FromUnixTimeSeconds(created) : null,
            ParseExpiration(model.ExpirationDate),
            size.TotalBillions,
            size.ActiveBillions,
            IsCodingSpecialist(model.Id, model.Description));
    }

    /// <summary>
    /// Phrases a provider uses for a model built to drive coding agents. "Agentic coding" alone is not
    /// one: on 2026-10-23 Liquid's blurb for LFM 2.5 said Liquid "advises against using it for agentic
    /// coding", and a general model saying what it is not for must not be demoted for saying so.
    /// </summary>
    private static readonly string[] CodingSpecialistTerms = ["coding agent", "agentic coding model"];

    /// <summary>Whole words of an id's slug that name a coding model: <c>north-mini-code</c>, <c>qwen3-coder</c>.</summary>
    private static readonly string[] CodingSpecialistIdWords = ["code", "coder"];

    /// <summary>
    /// Read from the description and the id, like the guardrail flag, because nothing structured says so.
    /// Matched on whole words of the id, so <c>codestral</c> and <c>codex</c> are not caught by it.
    /// </summary>
    private static bool IsCodingSpecialist(string id, string? description)
    {
        if (description is not null
            && CodingSpecialistTerms.Any(term => description.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var slug = id.Split(':', 2)[0];
        slug = slug[(slug.IndexOf('/', StringComparison.Ordinal) + 1)..];

        return slug.Split(['-', '_', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Any(word => CodingSpecialistIdWords.Contains(word, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Terms a provider uses for a safety classifier and, measured against the whole roster, for
    /// nothing else. Kept deliberately narrow — a rule broad enough to catch every future guardrail
    /// would also throw away ordinary models whose blurb mentions safety, and the cost of the two
    /// mistakes is not symmetric: a missed guardrail produces one strange answer that a thumbs-down
    /// records, while a wrongly excluded model is silently never used again.
    /// </summary>
    private static readonly string[] GuardrailTerms = ["guardrail", "content safety", "safeguard"];

    /// <summary>
    /// A guardrail model is a classifier wearing a chat model's clothes: text in, text out, free,
    /// long context. Nothing in the structured roster separates it from an assistant — no category,
    /// no flag — so the provider's own description is the only signal there is. Absent a description
    /// the model is taken at face value, because a provider that stops sending the field must not
    /// empty the roster.
    /// </summary>
    private static bool IsGuardrail(string? description) =>
        description is not null
        && GuardrailTerms.Any(term => description.Contains(term, StringComparison.OrdinalIgnoreCase));

    /// <summary>The API sends a bare date; treat it as end-of-day UTC so a model is usable all day.</summary>
    private static DateTimeOffset? ParseExpiration(string? value) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? new DateTimeOffset(date.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero)
            : null;

    [GeneratedRegex(@"\btool(?:s|_choice|_calls?|[ -]use|[ -]calling)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ToolTerms();

    [LoggerMessage(LogLevel.Debug, "Read AI model roster: {FreeCount} free of {TotalCount} total.")]
    static partial void LogRosterRead(ILogger logger, int freeCount, int totalCount);

    /// <summary>
    /// The provider explains every rejection in the response body — which model it disliked, which
    /// field was malformed — and the status code alone says none of it. Without this a 400 is
    /// indistinguishable from any other 400 and can only be diagnosed by replaying the request by hand.
    /// </summary>
    private static string Describe(ApiException exception)
    {
        var body = exception.Content;
        if (string.IsNullOrWhiteSpace(body))
        {
            return "(no response body)";
        }

        return body.Length > MaxLoggedBodyLength ? body[..MaxLoggedBodyLength] + "…" : body;
    }

    [LoggerMessage(LogLevel.Warning, "Failed to read the AI model roster (HTTP {StatusCode}): {Response}")]
    static partial void LogRosterFailed(ILogger logger, int statusCode, string response, Exception exception);

    [LoggerMessage(LogLevel.Warning, "AI chat completion failed (HTTP {StatusCode}): {Response}")]
    static partial void LogCompletionFailed(ILogger logger, int statusCode, string response, Exception exception);

    [LoggerMessage(LogLevel.Warning, "AI provider returned an in-band error {Code}: {Message}")]
    static partial void LogCompletionRejected(ILogger logger, int code, string message);

    [LoggerMessage(LogLevel.Warning, "AI model {Model} returned an empty completion.")]
    static partial void LogCompletionEmpty(ILogger logger, string model);

    [LoggerMessage(LogLevel.Information,
        "AI provider reported {Kind} without naming the head of the chain (it named {Model}); treating it as a failed request.")]
    static partial void LogModelFailureNotAtHead(ILogger logger, string model, string kind);

    [LoggerMessage(LogLevel.Debug, "Read provider health of AI model {Model}: {UptimePercent}% uptime over the last day.")]
    static partial void LogHealthRead(ILogger logger, string model, double? uptimePercent);

    // Information rather than a warning: the model is simply treated as healthy, which is how it was
    // ranked before health was read at all, and a provider hiccup would otherwise post one warning per model.
    [LoggerMessage(LogLevel.Information,
        "Could not read the provider health of AI model {Model} (HTTP {StatusCode}): {Response}. It counts as healthy.")]
    static partial void LogHealthFailed(ILogger logger, string model, int statusCode, string response);
}
