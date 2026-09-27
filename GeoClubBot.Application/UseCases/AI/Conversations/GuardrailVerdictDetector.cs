using System.Text.Json;
using System.Text.RegularExpressions;

namespace UseCases.UseCases.AI.Conversations;

/// <summary>
/// Recognises a safety classifier's verdict returned in place of an answer.
///
/// Guardrail models moderate other models' traffic, so a question routed to one comes back as a verdict
/// on the question — <c>User Safety: safe</c> — rather than a reply to it. The request succeeds and the
/// completion is well formed, so nothing upstream flags it; only its shape gives it away. Beta testers
/// were shown exactly that as an answer, three times.
///
/// Deliberately narrow: it fires only when the whole reply is a verdict and nothing else, so a real
/// answer that happens to mention road safety is never thrown away. Bare yes/no classifiers are left
/// out of reach on purpose, because "No." can be a genuine answer to a GeoGuessr question.
/// </summary>
public static partial class GuardrailVerdictDetector
{
    /// <summary>A verdict is a line or three; anything longer is prose, whatever it contains.</summary>
    private const int MaxVerdictLength = 400;

    /// <summary>Every label a verdict line may carry. A line with any other label is not a verdict.</summary>
    private static readonly HashSet<string> VerdictKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "user safety", "response safety", "prompt safety", "safety",
        "safety categories", "safety category", "categories", "category",
        "refusal", "response refusal", "harmful request", "harmful response"
    };

    /// <summary>The labels whose value is the verdict itself; at least one must be present.</summary>
    private static readonly HashSet<string> SafetyKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "user safety", "response safety", "prompt safety", "safety", "harmful request", "harmful response"
    };

    private static readonly HashSet<string> VerdictValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "safe", "unsafe", "controversial", "yes", "no", "harmful", "unharmful"
    };

    public static bool IsVerdict(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.Length > MaxVerdictLength)
        {
            return false;
        }

        return BareVerdict().IsMatch(trimmed) || IsJsonVerdict(trimmed) || IsLabelledVerdict(trimmed);
    }

    /// <summary>Some classifiers answer in JSON: <c>{"User Safety": "safe"}</c>.</summary>
    private static bool IsJsonVerdict(string text)
    {
        if (!text.StartsWith('{'))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var properties = document.RootElement.EnumerateObject().ToList();

            return properties.Count > 0
                   && properties.All(property => VerdictKeys.Contains(NormaliseKey(property.Name)))
                   && properties.Any(property => SafetyKeys.Contains(NormaliseKey(property.Name))
                                                 && property.Value.ValueKind == JsonValueKind.String
                                                 && IsVerdictValue(property.Value.GetString()));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// NVIDIA's (<c>User Safety: safe</c>), Qwen3Guard's (<c>Safety: Safe</c> + <c>Categories: None</c>)
    /// and WildGuard's (<c>Harmful request: no</c>) formats: every line a known label and a value, with
    /// at least one of them a verdict.
    /// </summary>
    private static bool IsLabelledVerdict(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var sawVerdict = false;

        foreach (var line in lines)
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                return false;
            }

            var key = NormaliseKey(line[..separator]);
            if (!VerdictKeys.Contains(key))
            {
                return false;
            }

            if (SafetyKeys.Contains(key) && IsVerdictValue(line[(separator + 1)..]))
            {
                sawVerdict = true;
            }
        }

        return sawVerdict;
    }

    /// <summary>Tolerates the decoration a chat template adds: bold markers, underscores, doubled spaces.</summary>
    private static string NormaliseKey(string key) =>
        Whitespace().Replace(key.Replace('_', ' ').Trim().Trim('*').Trim(), " ");

    private static bool IsVerdictValue(string? value) =>
        value is not null && VerdictValues.Contains(value.Trim().Trim('*').TrimEnd('.').Trim());

    /// <summary>Llama Guard: <c>safe</c>, or <c>unsafe</c> followed by a line of hazard codes such as <c>S1,S10</c>.</summary>
    [GeneratedRegex(@"^(?:safe|unsafe)(?:[ \t]*\r?\n[ \t]*S[0-9]{1,2}(?:[ \t]*,[ \t]*S[0-9]{1,2})*)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BareVerdict();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
