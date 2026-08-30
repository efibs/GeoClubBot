using System.Globalization;
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
    /// Most models one request may name. OpenRouter refuses a longer list outright with
    /// <c>'models' array must have 3 items or fewer</c>, so it is enforced here rather than trusted to
    /// configuration: a wire limit belongs with the wire format.
    /// </summary>
    private const int MaxModelsPerRequest = 3;

    /// <summary>Response bodies are logged for diagnosis, so cap what a failing provider can write to the log.</summary>
    private const int MaxLoggedBodyLength = 500;

    private IOpenRouterApi CreateApi() =>
        RestService.For<IOpenRouterApi>(httpClientFactory.CreateClient(HttpClientName));

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
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            LogRosterFailed(logger, 0, ex.Message, ex);
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
            return Error.Unexpected("ai.no_model_available", "No AI model is currently available.");
        }

        var payload = new OpenRouterChatRequestDto
        {
            Model = request.ModelChain[0],
            // Only send the fallback array when there is something to fall back to.
            Models = request.ModelChain.Count > 1 ? BuildFallbackChain(request.ModelChain) : null,
            Messages = [.. request.Messages.Select(ToMessageDto)],
            Temperature = request.Temperature,
            MaxTokens = request.MaxTokens
        };

        try
        {
            var response = await CreateApi().CreateChatCompletionAsync(payload, cancellationToken).ConfigureAwait(false);

            // OpenRouter reports some upstream failures in-band with HTTP 200, so a successful
            // response object is not by itself a successful completion.
            if (response.Error is { } error)
            {
                LogCompletionRejected(logger, error.Code ?? 0, error.Message ?? "(no message)");
                return Error.Unexpected("ai.chat_request_failed",
                    "The AI provider rejected the request. Please try again in a moment.");
            }

            var text = response.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(text))
            {
                LogCompletionEmpty(logger, response.Model ?? "(unknown)");
                return Error.Unexpected("ai.empty_response", "The AI model returned an empty response.");
            }

            return new AiChatResponse(
                text,
                response.Model ?? request.ModelChain[0],
                new AiTokenUsage(response.Usage?.PromptTokens ?? 0, response.Usage?.CompletionTokens ?? 0));
        }
        catch (ApiException ex)
        {
            LogCompletionFailed(logger, (int)ex.StatusCode, Describe(ex), ex);

            // 429 survives the whole chain only when every model is exhausted, so it is worth its own
            // message: the caller surfaces it as a budget problem rather than a generic failure.
            return (int)ex.StatusCode == 429
                ? Error.Conflict("ai.rate_limited", "The AI provider is rate-limiting us right now. Please try again shortly.")
                : Error.Unexpected("ai.chat_request_failed", "The AI provider could not answer that right now.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            LogCompletionFailed(logger, 0, ex.Message, ex);
            return Error.Unexpected("ai.chat_request_failed", "Could not reach the AI provider.");
        }
    }

    /// <summary>
    /// Trims an over-long chain to what the provider accepts, keeping the head and the final entry.
    /// The last entry is the fallback router, which is the one that is always reachable — dropping it
    /// to make room for a ranked model would trade the guarantee for a guess.
    /// </summary>
    private static List<string> BuildFallbackChain(IReadOnlyList<string> chain) =>
        chain.Count <= MaxModelsPerRequest
            ? [.. chain]
            : [.. chain.Take(MaxModelsPerRequest - 1), chain[^1]];

    private static OpenRouterMessageDto ToMessageDto(AiChatMessage message)
    {
        var role = message.Role switch
        {
            AiChatRole.System => "system",
            AiChatRole.Assistant => "assistant",
            _ => "user"
        };

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
        var parameters = model.SupportedParameters ?? [];

        return new ChatModelDescriptor(
            model.Id,
            model.Name ?? model.Id,
            model.ContextLength ?? model.TopProvider?.ContextLength ?? 0,
            model.TopProvider?.MaxCompletionTokens,
            modalities.Contains("image", StringComparer.OrdinalIgnoreCase),
            parameters.Contains("tools", StringComparer.OrdinalIgnoreCase),
            parameters.Contains("structured_outputs", StringComparer.OrdinalIgnoreCase),
            model.Created is { } created ? DateTimeOffset.FromUnixTimeSeconds(created) : null,
            ParseExpiration(model.ExpirationDate));
    }

    /// <summary>The API sends a bare date; treat it as end-of-day UTC so a model is usable all day.</summary>
    private static DateTimeOffset? ParseExpiration(string? value) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? new DateTimeOffset(date.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero)
            : null;

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
}
