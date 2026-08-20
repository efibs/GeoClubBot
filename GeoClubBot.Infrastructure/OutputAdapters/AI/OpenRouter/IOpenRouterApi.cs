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

    [Post("/api/v1/chat/completions")]
    Task<OpenRouterChatResponseDto> CreateChatCompletionAsync(
        [Body] OpenRouterChatRequestDto request,
        CancellationToken cancellationToken = default);
}
