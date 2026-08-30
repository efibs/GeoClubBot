using System.Text.Json.Serialization;

namespace Infrastructure.OutputAdapters.AI.OpenRouter.Dtos;

// OpenRouter's wire format is snake_case while Refit's default System.Text.Json settings expect
// camelCase, so every multi-word field needs an explicit name. Silently-null properties here would
// look like "no free models available" at runtime, which is why these are spelled out rather than
// relying on a naming policy.

public sealed class OpenRouterModelsResponseDto
{
    [JsonPropertyName("data")]
    public List<OpenRouterModelDto> Data { get; set; } = [];
}

public sealed class OpenRouterModelDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Unix seconds.</summary>
    [JsonPropertyName("created")]
    public long? Created { get; set; }

    [JsonPropertyName("context_length")]
    public int? ContextLength { get; set; }

    [JsonPropertyName("architecture")]
    public OpenRouterArchitectureDto? Architecture { get; set; }

    [JsonPropertyName("pricing")]
    public OpenRouterPricingDto? Pricing { get; set; }

    [JsonPropertyName("top_provider")]
    public OpenRouterTopProviderDto? TopProvider { get; set; }

    [JsonPropertyName("supported_parameters")]
    public List<string>? SupportedParameters { get; set; }

    /// <summary>
    /// Kept as a string: the API sends a bare date ("2026-09-30") which does not round-trip into
    /// DateTimeOffset through the default converter.
    /// </summary>
    [JsonPropertyName("expiration_date")]
    public string? ExpirationDate { get; set; }
}

public sealed class OpenRouterArchitectureDto
{
    [JsonPropertyName("input_modalities")]
    public List<string>? InputModalities { get; set; }

    [JsonPropertyName("output_modalities")]
    public List<string>? OutputModalities { get; set; }
}

public sealed class OpenRouterPricingDto
{
    /// <summary>Decimal string of USD per prompt token; "0" marks a free model.</summary>
    [JsonPropertyName("prompt")]
    public string? Prompt { get; set; }

    [JsonPropertyName("completion")]
    public string? Completion { get; set; }
}

public sealed class OpenRouterTopProviderDto
{
    [JsonPropertyName("context_length")]
    public int? ContextLength { get; set; }

    [JsonPropertyName("max_completion_tokens")]
    public int? MaxCompletionTokens { get; set; }

    [JsonPropertyName("is_moderated")]
    public bool? IsModerated { get; set; }
}
