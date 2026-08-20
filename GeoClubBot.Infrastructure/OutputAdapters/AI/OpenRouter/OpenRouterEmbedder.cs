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
                LogBatchRejected(logger, error.Message ?? "(no message)");
                return Error.Unexpected("ai.embedding_failed", "The embedding provider rejected the request.");
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
            LogBatchFailed(logger, (int)ex.StatusCode, ex);
            return (int)ex.StatusCode == 429
                ? Error.Conflict("ai.rate_limited", "The embedding provider is rate-limiting us right now.")
                : Error.Unexpected("ai.embedding_failed", "The embedding provider could not be reached.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            LogBatchFailed(logger, 0, ex);
            return Error.Unexpected("ai.embedding_failed", "The embedding provider could not be reached.");
        }
    }

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

    [LoggerMessage(LogLevel.Warning, "The embedding provider rejected a batch: {Message}")]
    static partial void LogBatchRejected(ILogger logger, string message);

    [LoggerMessage(LogLevel.Warning, "Embedding batch failed (HTTP {StatusCode}).")]
    static partial void LogBatchFailed(ILogger logger, int statusCode, Exception exception);

    [LoggerMessage(LogLevel.Warning, "Requested {Requested} embedding(s) but received {Received}.")]
    static partial void LogBatchCountMismatch(ILogger logger, int requested, int received);

    [LoggerMessage(LogLevel.Error, "Embedding model {ModelId} returned {Actual} dimensions but {Expected} are configured.")]
    static partial void LogDimensionMismatch(ILogger logger, string modelId, int expected, int actual);
}
