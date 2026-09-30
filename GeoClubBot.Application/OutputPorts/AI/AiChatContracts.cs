namespace UseCases.OutputPorts.AI;

public enum AiChatRole
{
    System = 0,
    User,
    Assistant
}

/// <summary>One piece of a message. Messages are multimodal, so a single turn mixes text and images.</summary>
public abstract record AiContentPart;

public sealed record AiTextPart(string Text) : AiContentPart;

/// <summary>
/// An image referenced by URL. Remote <c>https</c> URLs are preferred over <c>data:</c> URIs — the
/// provider fetches them itself, which avoids base64-inflating every request by ~33%.
/// </summary>
public sealed record AiImagePart(string Url) : AiContentPart;

public sealed record AiChatMessage(AiChatRole Role, IReadOnlyList<AiContentPart> Parts)
{
    public static AiChatMessage System(string text) =>
        new(AiChatRole.System, [new AiTextPart(text)]);

    public static AiChatMessage Assistant(string text) =>
        new(AiChatRole.Assistant, [new AiTextPart(text)]);

    public static AiChatMessage User(string text, IEnumerable<string>? imageUrls = null)
    {
        var parts = new List<AiContentPart> { new AiTextPart(text) };
        if (imageUrls is not null)
        {
            parts.AddRange(imageUrls.Select(url => new AiImagePart(url)));
        }

        return new AiChatMessage(AiChatRole.User, parts);
    }

    /// <summary>Plain-text projection, used for length budgeting and logging.</summary>
    public string ToPlainText() =>
        string.Join(" ", Parts.OfType<AiTextPart>().Select(part => part.Text));
}

/// <summary>
/// A completion request. <paramref name="ModelChain"/> is ordered: the first entry is the preferred
/// model and the rest are server-side fallbacks, so one HTTP call survives a model being down,
/// rate-limited, or refusing on moderation grounds.
/// </summary>
public sealed record AiChatRequest(
    IReadOnlyList<string> ModelChain,
    IReadOnlyList<AiChatMessage> Messages,
    double? Temperature = null,
    int? MaxTokens = null);

public sealed record AiTokenUsage(int PromptTokens, int CompletionTokens);

/// <summary>
/// <paramref name="ModelUsed"/> is the model that actually answered, which may be any entry from the
/// chain. Recording it is what makes "the bot got worse today" diagnosable.
/// </summary>
public sealed record AiChatResponse(string Text, string ModelUsed, AiTokenUsage Usage);

/// <summary>
/// Codes a chat completion fails with. The caller acts on them differently: some say the models it
/// named failed, and are worth retrying against others; some say the provider was never reached or is
/// throttling us, where a retry only adds to the wait and no model deserves the blame.
/// </summary>
public static class ChatErrorCodes
{
    /// <summary>Every model in the chain was rate-limited, after the pipeline had already waited once.</summary>
    public const string RateLimited = "ai.rate_limited";

    /// <summary>
    /// The models failed: the provider answered with a server error, or with one reported in-band
    /// after failing over through the chain.
    /// </summary>
    public const string RequestFailed = "ai.chat_request_failed";

    /// <summary>
    /// The provider refused the request as sent — typically a replayed screenshot whose link has
    /// expired, or a body too large. The same request fails the same way whichever models it names.
    /// </summary>
    public const string Rejected = "ai.chat_request_rejected";

    /// <summary>
    /// No response at all: a network failure, a timeout, or the resilience pipeline refusing to send
    /// (an open circuit, a full limiter queue). Says nothing about the models named.
    /// </summary>
    public const string Unreachable = "ai.provider_unreachable";

    /// <summary>The model answered with nothing.</summary>
    public const string EmptyResponse = "ai.empty_response";

    /// <summary>There was no model to ask.</summary>
    public const string NoModelAvailable = "ai.no_model_available";

    /// <summary>
    /// A completion arrived but is not an answer — a safety classifier's verdict, or a reply from a
    /// model that should never have been asked. Raised by the use case, not by the client.
    /// </summary>
    public const string UnusableAnswer = "ai.unusable_answer";
}
