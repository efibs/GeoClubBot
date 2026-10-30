using System.Text.Json;
using System.Text.RegularExpressions;

namespace UseCases.UseCases.AI.Conversations;

/// <summary>
/// Recognises a tool call written out as text where an answer should be.
///
/// After its last tool round the model is asked with tool_choice "none", and in the answer replay of
/// 2026-10-29 nemotron-3-ultra then wrote the call it still wanted as plain text in 6 of the 11 answers
/// that got that far (#17, #24, #26, #27, #32, #42: 12 % of all 49):
/// <c>&lt;tool_call&gt;\n&lt;function=search_guides&gt;\n&lt;parameter=country&gt;\nPhilippines…</c>.
/// The completion was well formed, not empty and no classifier's verdict, so it was posted as the
/// answer under "This answer cites no guide". Asked without tools, the same model answered #17 and #24
/// correctly.
///
/// Recognised are the chat-template tokens models write calls with — <c>&lt;tool_call&gt;</c>,
/// <c>&lt;function=</c>, <c>&lt;parameter=</c>, <c>&lt;|tool_call_begin|&gt;</c>-style tokens and
/// Mistral's <c>[TOOL_CALLS]</c> — anywhere in the reply, since a reply holding any of them would show
/// them to the reader; and a reply that is nothing but a JSON call of one of the tools, fenced or not.
/// A sentence that only names a tool ("search_guides found nothing") is an answer like any other.
///
/// Pure, so each shape is pinned by a test rather than rediscovered in a posted answer.
/// </summary>
public static partial class ToolCallMarkupDetector
{
    /// <summary>The keys a JSON call names its tool by.</summary>
    private static readonly string[] NameKeys = ["name", "tool", "tool_name", "function"];

    /// <summary>The keys a JSON call carries its arguments under; a bare name is a mention, not a call.</summary>
    private static readonly string[] ArgumentKeys = ["arguments", "parameters", "args", "input"];

    /// <summary>The keys a JSON call is wrapped in: OpenAI's own shape, written out.</summary>
    private static readonly string[] WrapperKeys = ["tool_calls", "tool_call", "function_call", "function"];

    /// <summary>Wrappers within wrappers are read this deep; anything deeper is not a call.</summary>
    private const int MaxDepth = 3;

    /// <param name="toolNames">The tools that may have been declared: a JSON reply naming one is a call.</param>
    public static bool IsToolCallMarkup(string? text, IReadOnlyCollection<string> toolNames)
    {
        ArgumentNullException.ThrowIfNull(toolNames);

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return MarkupToken().IsMatch(text) || IsJsonCall(text.Trim(), toolNames);
    }

    private static bool IsJsonCall(string text, IReadOnlyCollection<string> toolNames)
    {
        var fenced = Fence().Match(text);
        var body = (fenced.Success ? fenced.Groups["body"].Value : text).Trim();
        if (body.Length == 0 || (body[0] != '{' && body[0] != '['))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return IsCall(document.RootElement, toolNames, depth: 0);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsCall(JsonElement element, IReadOnlyCollection<string> toolNames, int depth)
    {
        if (depth > MaxDepth)
        {
            return false;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                return element.GetArrayLength() > 0
                       && element.EnumerateArray().All(item => IsCall(item, toolNames, depth + 1));

            case JsonValueKind.Object:
                foreach (var key in WrapperKeys)
                {
                    if (element.TryGetProperty(key, out var inner)
                        && inner.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                        && IsCall(inner, toolNames, depth + 1))
                    {
                        return true;
                    }
                }

                return NameKeys.Any(key => element.TryGetProperty(key, out var name)
                                           && name.ValueKind == JsonValueKind.String
                                           && toolNames.Contains(name.GetString() ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                       && ArgumentKeys.Any(key => element.TryGetProperty(key, out _));

            default:
                return false;
        }
    }

    // <tool_call> and </tool_call>; <function=name> and <parameter=name> (Qwen's and nemotron's
    // template); <|tool_call_begin|>, <|tool_calls_section_begin|>, <｜tool▁calls▁begin｜> (Kimi's and
    // DeepSeek's, the latter in full-width bars); Mistral's [TOOL_CALLS].
    [GeneratedRegex(
        @"<\s*/?\s*tool_call\s*>"
        + @"|<\s*function\s*=\s*[A-Za-z_]"
        + @"|<\s*parameter\s*=\s*[A-Za-z_]"
        + @"|<\s*[|｜]\s*tool[_▁ ]?calls?[^>\n]{0,40}[|｜]\s*>"
        + @"|\[TOOL_CALLS\]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MarkupToken();

    [GeneratedRegex(@"^```[A-Za-z0-9_-]*[ \t]*\r?\n?(?<body>[\s\S]*?)\r?\n?```$", RegexOptions.CultureInvariant)]
    private static partial Regex Fence();
}
