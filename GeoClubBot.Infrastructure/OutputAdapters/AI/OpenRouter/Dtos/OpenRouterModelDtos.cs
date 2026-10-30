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

    /// <summary>
    /// Prose written by the provider. Read to recognise guardrail models and coding specialists, which
    /// declare exactly the same shape as a chat model and can be told apart no other way, and for the
    /// parameter counts many ids leave out ("16B active parameters out of 280B total").
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

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

/// <summary>
/// <c>GET /api/v1/models/{author}/{slug}/endpoints</c>: one model and every provider endpoint serving
/// it. Public and free — no key, no chat allowance — which is what makes reading it on every catalog
/// refresh affordable.
/// </summary>
public sealed class OpenRouterModelEndpointsResponseDto
{
    [JsonPropertyName("data")]
    public OpenRouterModelEndpointsDto? Data { get; set; }
}

public sealed class OpenRouterModelEndpointsDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("endpoints")]
    public List<OpenRouterEndpointDto>? Endpoints { get; set; }
}

public sealed class OpenRouterEndpointDto
{
    [JsonPropertyName("provider_name")]
    public string? ProviderName { get; set; }

    /// <summary>
    /// Percent of the last day's requests the endpoint served. Free models usually have exactly one
    /// endpoint, so this is how healthy the model is. Absent for an endpoint with no traffic to measure.
    /// </summary>
    [JsonPropertyName("uptime_last_1d")]
    public double? UptimeLastDay { get; set; }
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
