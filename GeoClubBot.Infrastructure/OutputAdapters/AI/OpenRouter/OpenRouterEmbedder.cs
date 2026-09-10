using System.Text.Json;
using Configuration;
using Infrastructure.OutputAdapters.AI.OpenRouter.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Refit;
using UseCases.OutputPorts.AI;
using Utilities;

namespace Infrastructure.OutputAdapters.AI.OpenRouter;

/// <summary>
/// Embeds text and images through OpenRouter's embeddings endpoint.
///
/// Builds its Refit proxy per call from <see cref="IHttpClientFactory"/> for the same reason as
/// <see cref="RefitChatModelClient"/>: this is a singleton, and holding one HttpClient forever would
/// pin a single handler and defeat rotation.
/// </summary>
public partial class OpenRouterEmbedder(
    IHttpClientFactory httpClientFactory,
    IOptions<AiConfiguration> configuration,
    ILogger<OpenRouterEmbedder> logger) : IEmbedder
{
    public string ModelId => configuration.Value.OpenRouter.EmbeddingModelId;

    public int Dimensions => configuration.Value.OpenRouter.EmbeddingDimensions;

    public async Task<Result<IReadOnlyList<ReadOnlyMemory<float>>>> EmbedAsync(
        IReadOnlyList<EmbeddingInput> inputs,
        CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0)
        {
            return Array.Empty<ReadOnlyMemory<float>>();
        }

        var openRouter = configuration.Value.OpenRouter;
        var batchSize = Math.Max(1, openRouter.EmbeddingBatchSize);
        var results = new List<ReadOnlyMemory<float>>(inputs.Count);

        foreach (var batch in inputs.Chunk(batchSize))
        {
            var batchResult = await EmbedBatchAsync(batch, openRouter, cancellationToken).ConfigureAwait(false);
            if (batchResult.IsFailure)
            {
                return batchResult.Error;
            }

            results.AddRange(batchResult.Value);
        }

        return results;
    }

    private async Task<Result<IReadOnlyList<ReadOnlyMemory<float>>>> EmbedBatchAsync(
        EmbeddingInput[] batch,
        OpenRouterConfiguration openRouter,
        CancellationToken cancellationToken)
    {
        var payload = new OpenRouterEmbeddingRequestDto
        {
            Model = openRouter.EmbeddingModelId,
            Input = [.. batch.Select(ToInputDto)]
        };

        try
        {
            var api = RestService.For<IOpenRouterApi>(
                httpClientFactory.CreateClient(RefitChatModelClient.HttpClientName));

            var response = await api.CreateEmbeddingsAsync(payload, cancellationToken).ConfigureAwait(false);

            if (response.Error is { } error)
            {
                // A single unreachable image URL fails the entire batch, so the message is worth
                // surfacing verbatim — it names the offending URL.
                LogBatchRejected(logger, error.Code ?? 0, error.Message ?? "(no message)");
                return Classify(error.Code ?? 0, error.Message);
            }

            if (response.Data is not { Count: > 0 } data || data.Count != batch.Length)
            {
                LogBatchCountMismatch(logger, batch.Length, response.Data?.Count ?? 0);
                return Error.Unexpected("ai.embedding_incomplete",
                    "The embedding provider returned a different number of vectors than requested.");
            }

            // Results are not guaranteed to arrive in request order; realign by index before use, or
            // vectors silently attach to the wrong chunks.
            var ordered = data.OrderBy(item => item.Index).ToList();

            var vectors = new List<ReadOnlyMemory<float>>(ordered.Count);
            foreach (var item in ordered)
            {
                if (item.Embedding is not { Length: > 0 } embedding)
                {
                    return Error.Unexpected("ai.embedding_incomplete", "The embedding provider returned an empty vector.");
                }

                if (embedding.Length != openRouter.EmbeddingDimensions)
                {
                    // Configured dimensions drive the vector collection's schema, so a mismatch would
                    // be rejected by the store or, worse, corrupt an existing index.
                    LogDimensionMismatch(logger, openRouter.EmbeddingModelId, openRouter.EmbeddingDimensions, embedding.Length);
                    return Error.Unexpected("ai.embedding_dimension_mismatch",
                        $"Embedding model returned {embedding.Length} dimensions but {openRouter.EmbeddingDimensions} are configured.");
                }

                vectors.Add(embedding);
            }

            LogBatchEmbedded(logger, vectors.Count, response.Usage?.PromptTokens ?? 0);
            return vectors;
        }
        catch (ApiException ex)
        {
            LogBatchFailed(logger, (int)ex.StatusCode, Describe(ex), ex);
            return Classify((int)ex.StatusCode, ReadProviderMessage(ex));
        }
        catch (ApiRequestException ex)
        {
            // Refit wraps every failure to get a response — a timeout, a refused connection, a request the
            // resilience pipeline declined to send — in this type, so catching the inner exception types
            // directly let all of them escape.
            LogBatchFailed(logger, 0, ex.InnerException?.Message ?? ex.Message, ex);
            return Error.Unexpected(EmbeddingErrorCodes.Failed, "The embedding provider could not be reached.");
        }
    }

    /// <summary>
    /// Sorts a provider error by what a caller can do about it.
    ///
    /// Only statuses that describe the request itself count as a rejection — an image the provider could
    /// not fetch arrives as a 400 — which is what lets a caller narrow a rejected batch down to the input
    /// at fault. Everything else is about the moment and worth trying again later. Before this, every
    /// status but 429 was reported as "could not be reached", including rejections from a provider that
    /// had been reached perfectly well.
    /// </summary>
    private static Error Classify(int statusCode, string? providerMessage)
    {
        var detail = string.IsNullOrWhiteSpace(providerMessage)
            ? string.Empty
            : $": {Truncate(providerMessage.Trim(), MaxErrorDetailLength)}";

        return statusCode switch
        {
            429 => Error.Conflict(EmbeddingErrorCodes.RateLimited,
                "The embedding provider is rate-limiting us right now."),
            400 or 413 or 415 or 422 => Error.Validation(EmbeddingErrorCodes.Rejected,
                $"The embedding provider rejected the request (HTTP {statusCode}){detail}"),
            > 0 => Error.Unexpected(EmbeddingErrorCodes.Failed,
                $"The embedding provider failed (HTTP {statusCode}){detail}"),
            _ => Error.Unexpected(EmbeddingErrorCodes.Failed,
                $"The embedding provider reported an error{detail}")
        };
    }

    /// <summary>The provider's own explanation from an error body, when it sent one it could be read from.</summary>
    private static string? ReadProviderMessage(ApiException exception)
    {
        if (string.IsNullOrWhiteSpace(exception.Content))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(exception.Content);

            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("error", out var error)
                   && error.ValueKind == JsonValueKind.Object
                   && error.TryGetProperty("message", out var message)
                   && message.ValueKind == JsonValueKind.String
                ? message.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length > maxLength ? value[..maxLength] + "…" : value;

    /// <summary>
    /// Provider detail carried into an error message. It reaches a Discord reply and a source's recorded
    /// failure reason, so it names what went wrong without pasting a whole response body.
    /// </summary>
    private const int MaxErrorDetailLength = 200;

    private static OpenRouterEmbeddingInputDto ToInputDto(EmbeddingInput input) => input switch
    {
        TextEmbeddingInput text => new OpenRouterEmbeddingInputDto
        {
            Content = [new OpenRouterContentPartDto { Type = "text", Text = text.Text }]
        },
        ImageEmbeddingInput image => new OpenRouterEmbeddingInputDto
        {
            Content =
            [
                new OpenRouterContentPartDto
                {
                    Type = "image_url",
                    ImageUrl = new OpenRouterImageUrlDto { Url = image.ImageUrl }
                }
            ]
        },
        _ => throw new NotSupportedException($"Unsupported embedding input '{input.GetType().Name}'.")
    };

    [LoggerMessage(LogLevel.Debug, "Embedded {VectorCount} input(s) using {TokenCount} token(s).")]
    static partial void LogBatchEmbedded(ILogger logger, int vectorCount, int tokenCount);

    [LoggerMessage(LogLevel.Warning, "The embedding provider reported an in-band error {Code}: {Message}")]
    static partial void LogBatchRejected(ILogger logger, int code, string message);

    /// <summary>
    /// The provider names the offending input or model in the response body; the status code alone
    /// leaves a 400 indistinguishable from any other and only diagnosable by replaying the request.
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

    /// <summary>Bodies are logged for diagnosis, so cap what a failing provider can write to the log.</summary>
    private const int MaxLoggedBodyLength = 500;

    [LoggerMessage(LogLevel.Warning, "Embedding batch failed (HTTP {StatusCode}): {Response}")]
    static partial void LogBatchFailed(ILogger logger, int statusCode, string response, Exception exception);

    [LoggerMessage(LogLevel.Warning, "Requested {Requested} embedding(s) but received {Received}.")]
    static partial void LogBatchCountMismatch(ILogger logger, int requested, int received);

    [LoggerMessage(LogLevel.Error, "Embedding model {ModelId} returned {Actual} dimensions but {Expected} are configured.")]
    static partial void LogDimensionMismatch(ILogger logger, string modelId, int expected, int actual);
}
