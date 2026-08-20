using System.Text.Json.Serialization;

namespace Infrastructure.OutputAdapters.AI.OpenRouter.Dtos;

public sealed class OpenRouterChatRequestDto
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Ordered fallback chain. OpenRouter retries the next entry server-side when a model errors, is
    /// rate-limited, or refuses on moderation grounds — one HTTP call, no client-side retry loop.
    /// </summary>
    [JsonPropertyName("models")]
    public List<string>? Models { get; set; }

    [JsonPropertyName("messages")]
    public List<OpenRouterMessageDto> Messages { get; set; } = [];

    [JsonPropertyName("temperature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Temperature { get; set; }

    [JsonPropertyName("max_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxTokens { get; set; }
}

public sealed class OpenRouterMessageDto
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "user";

    /// <summary>
    /// Either a plain string or a list of <see cref="OpenRouterContentPartDto"/>. The multipart form
    /// is only used when a turn actually carries images: some providers are stricter about accepting
    /// an array for system/assistant roles than the OpenAI spec suggests.
    /// </summary>
    [JsonPropertyName("content")]
    public object Content { get; set; } = string.Empty;
}

public sealed class OpenRouterContentPartDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonPropertyName("image_url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenRouterImageUrlDto? ImageUrl { get; set; }
}

public sealed class OpenRouterImageUrlDto
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}

public sealed class OpenRouterChatResponseDto
{
    /// <summary>The model that actually answered, which may be any entry from the requested chain.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("choices")]
    public List<OpenRouterChoiceDto>? Choices { get; set; }

    [JsonPropertyName("usage")]
    public OpenRouterUsageDto? Usage { get; set; }

    /// <summary>OpenRouter reports some upstream failures in-band with HTTP 200.</summary>
    [JsonPropertyName("error")]
    public OpenRouterErrorDto? Error { get; set; }
}

public sealed class OpenRouterChoiceDto
{
    [JsonPropertyName("message")]
    public OpenRouterResponseMessageDto? Message { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

public sealed class OpenRouterResponseMessageDto
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

public sealed class OpenRouterUsageDto
{
    [JsonPropertyName("prompt_tokens")]
    public int? PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int? CompletionTokens { get; set; }
}

public sealed class OpenRouterErrorDto
{
    [JsonPropertyName("code")]
    public int? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
