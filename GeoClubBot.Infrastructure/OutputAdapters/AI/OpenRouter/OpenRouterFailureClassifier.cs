using System.Text.Json;
using System.Text.RegularExpressions;

namespace Infrastructure.OutputAdapters.AI.OpenRouter;

/// <summary>
/// Reads what an OpenRouter error says about <em>who</em> failed, which its status code does not.
///
/// A 403 normally means the request itself was refused, and a 429 that our key is out of allowance —
/// and either would fail the same way against any model. Two failures measured on 2026-10-23 look the
/// same from the status code and are nothing of the kind: free models reserved for agentic harnesses
/// answer every other client with 403, and a model whose upstream provider has run out of capacity in
/// the pool shared by every free user answers 429. Both are one model's trouble; the next model in
/// line answers the same request. Only the body tells them apart.
///
/// Shared by the chat client, which maps them to their own error codes, and the resilience pipeline,
/// which must not wait a minute for our rate-limit window to turn over when the limit was never ours.
/// </summary>
public static partial class OpenRouterFailureClassifier
{
    /// <summary>The <c>metadata.limit_source</c> of a 429 that came from the provider's pool, not our key.</summary>
    public const string UpstreamSharedPoolLimitSource = "upstream_provider_shared_pool";

    private const string AgenticHarnessPhrase = "only available on agentic harnesses";

    /// <summary>The routing step named in a gated model's 403, kept as a second signal should the wording change.</summary>
    private const string AgenticHarnessRoutingStep = "agentic harness";

    private const string UpstreamRateLimitPhrase = "rate-limited upstream";

    /// <summary>Classifies an error response body, as Refit hands it over. Anything unparseable is <see cref="OpenRouterFailureKind.Other"/>.</summary>
    public static OpenRouterFailure Classify(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return OpenRouterFailure.Other;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("error", out var error)
                || error.ValueKind != JsonValueKind.Object)
            {
                return OpenRouterFailure.Other;
            }

            return Classify(
                ReadString(error, "message"),
                error.TryGetProperty("metadata", out var metadata) ? metadata : null);
        }
        catch (JsonException)
        {
            // A proxy's HTML error page, a truncated body: nothing to learn from, and no reason to throw.
            return OpenRouterFailure.Other;
        }
    }

    /// <summary>Classifies an error reported in-band, inside an otherwise successful response.</summary>
    public static OpenRouterFailure Classify(string? message, JsonElement? metadata)
    {
        var raw = ReadString(metadata, "raw");

        if (Contains(message, AgenticHarnessPhrase)
            || Contains(ReadString(metadata, "failed_routing_step"), AgenticHarnessRoutingStep))
        {
            return new OpenRouterFailure(OpenRouterFailureKind.AgenticHarnessGate, NamedModel(message) ?? NamedModel(raw));
        }

        if (string.Equals(ReadString(metadata, "limit_source"), UpstreamSharedPoolLimitSource, StringComparison.OrdinalIgnoreCase)
            || Contains(raw, UpstreamRateLimitPhrase)
            || Contains(message, UpstreamRateLimitPhrase))
        {
            // The outer message is a generic "Provider returned error"; the provider's own text names the model.
            return new OpenRouterFailure(OpenRouterFailureKind.UpstreamRateLimit, NamedModel(raw) ?? NamedModel(message));
        }

        return OpenRouterFailure.Other;
    }

    /// <summary>For the resilience pipeline, which sees only the body of a 429.</summary>
    public static bool IsUpstreamRateLimit(string? body) =>
        Classify(body).Kind == OpenRouterFailureKind.UpstreamRateLimit;

    private static string? ReadString(JsonElement? element, string property) =>
        element is { ValueKind: JsonValueKind.Object } value
        && value.TryGetProperty(property, out var found)
        && found.ValueKind == JsonValueKind.String
            ? found.GetString()
            : null;

    private static bool Contains(string? text, string phrase) =>
        text?.Contains(phrase, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// The model a message opens with — "google/gemma-4-31b-it:free is temporarily rate-limited
    /// upstream" — or null when it names none.
    /// </summary>
    private static string? NamedModel(string? text) =>
        text is not null && LeadingModelId().Match(text) is { Success: true } match
            ? match.Groups["id"].Value
            : null;

    [GeneratedRegex(@"^\s*(?<id>[^\s/]+/\S+)\s+is\s", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingModelId();
}

public enum OpenRouterFailureKind
{
    /// <summary>Nothing the body says changes what the status code means.</summary>
    Other = 0,

    /// <summary>The model is reserved for agentic harnesses and will never serve this bot.</summary>
    AgenticHarnessGate,

    /// <summary>The model's upstream provider is out of the capacity it shares among free users.</summary>
    UpstreamRateLimit
}

/// <param name="NamedModelId">The model the provider's message names, or null when it names none.</param>
public sealed record OpenRouterFailure(OpenRouterFailureKind Kind, string? NamedModelId)
{
    public static readonly OpenRouterFailure Other = new(OpenRouterFailureKind.Other, null);

    /// <summary>Whether the message names the chain's head — the model the request sent as its <c>model</c>.</summary>
    public bool NamesHead(IReadOnlyList<string> chain) =>
        chain.Count > 0
        && NamedModelId is not null
        && string.Equals(NamedModelId, chain[0], StringComparison.OrdinalIgnoreCase);
}
