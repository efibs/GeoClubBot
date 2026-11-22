using System.Text.Json.Serialization;
using Infrastructure.OutputAdapters.AI.OpenRouter.Dtos;

namespace Infrastructure.OutputAdapters.AI.OpenRouter;

/// <summary>
/// The JSON metadata <see cref="IOpenRouterApi"/>'s generated Refit client reads and writes with (#533).
/// The options are Refit's own defaults, as in <c>GeoGuessrJsonContext</c>; the DTOs name every field
/// themselves, so the naming policy only matters for one they forget.
/// <see cref="OpenRouterMessageDto.Content"/> is an <see cref="object"/> holding a string or a list of
/// content parts, so both are listed: a runtime type the context does not know fails the request.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    AllowDuplicateProperties = false)]
[JsonSerializable(typeof(OpenRouterModelsResponseDto))]
[JsonSerializable(typeof(OpenRouterModelEndpointsResponseDto))]
[JsonSerializable(typeof(OpenRouterChatRequestDto))]
[JsonSerializable(typeof(OpenRouterChatResponseDto))]
[JsonSerializable(typeof(OpenRouterEmbeddingRequestDto))]
[JsonSerializable(typeof(OpenRouterEmbeddingResponseDto))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(List<OpenRouterContentPartDto>))]
public sealed partial class OpenRouterJsonContext : JsonSerializerContext;
