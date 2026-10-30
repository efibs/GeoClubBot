using System.Text.Json;

namespace UseCases.UseCases.AI.Tools;

/// <summary>
/// The tool calls behind an answer as the conversation store keeps them, and as the feedback export
/// reads them back: one JSON array of <see cref="AiToolCallSummary"/>, camel-cased like the export.
///
/// Reading never fails. The text is the bot's own, but a turn stored by a later version, or edited by
/// hand, must not cost an export its other records; it comes back as no calls instead.
/// </summary>
public static class AiToolCallLog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>The calls as stored, or null when there were none.</summary>
    public static string? Serialize(IReadOnlyList<AiToolCallSummary>? calls) =>
        calls is { Count: > 0 } ? JsonSerializer.Serialize(calls, Options) : null;

    /// <summary>The calls a stored text holds; empty for none, and for a text that cannot be read.</summary>
    public static IReadOnlyList<AiToolCallSummary> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<AiToolCallSummary>>(json, Options) is { } calls
                ? [.. calls.Where(call => call is not null)]
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
