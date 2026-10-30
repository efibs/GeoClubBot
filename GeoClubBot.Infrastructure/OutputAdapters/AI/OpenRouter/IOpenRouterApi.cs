using Infrastructure.OutputAdapters.AI.OpenRouter.Dtos;
using Refit;

namespace Infrastructure.OutputAdapters.AI.OpenRouter;

/// <summary>
/// Declarative binding to the OpenRouter REST API, mirroring how <c>IGeoGuessrApi</c> wraps the
/// GeoGuessr API. Authentication, base address and resilience are configured on the named HttpClient.
/// </summary>
public interface IOpenRouterApi
{
    [Get("/api/v1/models")]
    Task<OpenRouterModelsResponseDto> ReadModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The provider endpoints serving one model. <paramref name="slug"/> keeps its variant suffix
    /// (<c>inkling:free</c>): the free variant's endpoints are the ones the bot is routed to, and the
    /// escaped colon is accepted.
    /// </summary>
    [Get("/api/v1/models/{author}/{slug}/endpoints")]
    Task<OpenRouterModelEndpointsResponseDto> ReadModelEndpointsAsync(
        string author,
        string slug,
        CancellationToken cancellationToken = default);

    [Post("/api/v1/chat/completions")]
    Task<OpenRouterChatResponseDto> CreateChatCompletionAsync(
        [Body] OpenRouterChatRequestDto request,
        CancellationToken cancellationToken = default);

    [Post("/api/v1/embeddings")]
    Task<OpenRouterEmbeddingResponseDto> CreateEmbeddingsAsync(
        [Body] OpenRouterEmbeddingRequestDto request,
        CancellationToken cancellationToken = default);
}
