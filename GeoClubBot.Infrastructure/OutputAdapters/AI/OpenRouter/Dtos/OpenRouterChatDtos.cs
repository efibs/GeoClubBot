using System.Text.Json;
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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Models { get; set; }

    [JsonPropertyName("messages")]
    public List<OpenRouterMessageDto> Messages { get; set; } = [];

    [JsonPropertyName("temperature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Temperature { get; set; }

    [JsonPropertyName("max_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxTokens { get; set; }

    /// <summary>The tools the model may call; left out of the body when there are none.</summary>
    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OpenRouterToolDto>? Tools { get; set; }

    /// <summary>"auto" or "none", sent only beside <see cref="Tools"/>. Never "required": dots-3-note-preview refuses it.</summary>
    [JsonPropertyName("tool_choice")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolChoice { get; set; }
}

public sealed class OpenRouterMessageDto
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "user";

    /// <summary>
    /// Either a plain string or a list of <see cref="OpenRouterContentPartDto"/>. The multipart form
    /// is only used when a turn actually carries images: some providers are stricter about accepting
    /// an array for system/assistant roles than the OpenAI spec suggests. Null — written as null, not
    /// left out — for an assistant turn that only called tools, as the OpenAI format has it.
    /// </summary>
    [JsonPropertyName("content")]
    public object? Content { get; set; } = string.Empty;

    /// <summary>The tools an assistant turn called, sent back with their results.</summary>
    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OpenRouterToolCallDto>? ToolCalls { get; set; }

    /// <summary>The call a <c>tool</c> message answers.</summary>
    [JsonPropertyName("tool_call_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallId { get; set; }
}

public sealed class OpenRouterToolDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public OpenRouterFunctionDto Function { get; set; } = new();
}

public sealed class OpenRouterFunctionDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>A JSON schema object, sent as JSON rather than as a string of it.</summary>
    [JsonPropertyName("parameters")]
    public JsonElement Parameters { get; set; }
}

public sealed class OpenRouterToolCallDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public OpenRouterFunctionCallDto? Function { get; set; }
}

public sealed class OpenRouterFunctionCallDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// A string holding the JSON arguments, as the OpenAI format has it — but read as raw JSON, so a
    /// provider that sends the object itself does not fail the whole response.
    /// </summary>
    [JsonPropertyName("arguments")]
    public JsonElement? Arguments { get; set; }
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

    [JsonPropertyName("tool_calls")]
    public List<OpenRouterToolCallDto>? ToolCalls { get; set; }
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

    /// <summary>
    /// Kept as raw JSON: its shape differs with the failure (<c>raw</c> is a provider's text for one,
    /// an object for another), and a typed member that guessed wrong would fail the whole response.
    /// Read by <see cref="OpenRouterFailureClassifier"/>.
    /// </summary>
    [JsonPropertyName("metadata")]
    public JsonElement? Metadata { get; set; }
}
