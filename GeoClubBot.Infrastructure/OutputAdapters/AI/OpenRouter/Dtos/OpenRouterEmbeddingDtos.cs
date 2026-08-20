using System.Text.Json.Serialization;

namespace Infrastructure.OutputAdapters.AI.OpenRouter.Dtos;

public sealed class OpenRouterEmbeddingRequestDto
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// One entry per thing to embed. Entries are objects rather than bare strings so text and image
    /// inputs share a shape; the provider returns one vector per entry, index-aligned.
    /// </summary>
    [JsonPropertyName("input")]
    public List<OpenRouterEmbeddingInputDto> Input { get; set; } = [];

    [JsonPropertyName("encoding_format")]
    public string EncodingFormat { get; set; } = "float";
}

public sealed class OpenRouterEmbeddingInputDto
{
    [JsonPropertyName("content")]
    public List<OpenRouterContentPartDto> Content { get; set; } = [];
}

public sealed class OpenRouterEmbeddingResponseDto
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("data")]
    public List<OpenRouterEmbeddingDataDto>? Data { get; set; }

    [JsonPropertyName("usage")]
    public OpenRouterEmbeddingUsageDto? Usage { get; set; }

    [JsonPropertyName("error")]
    public OpenRouterErrorDto? Error { get; set; }
}

public sealed class OpenRouterEmbeddingDataDto
{
    /// <summary>
    /// Position in the request's input array. The provider is not required to return entries in
    /// order, so results are re-sorted by this before being handed back.
    /// </summary>
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("embedding")]
    public float[]? Embedding { get; set; }
}

public sealed class OpenRouterEmbeddingUsageDto
{
    [JsonPropertyName("prompt_tokens")]
    public int? PromptTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int? TotalTokens { get; set; }
}
