namespace UseCases.OutputPorts.AI;

public enum AiChatRole
{
    System = 0,
    User,
    Assistant,

    /// <summary>The result of one tool call, answering the assistant message that asked for it.</summary>
    Tool
}

/// <summary>One piece of a message. Messages are multimodal, so a single turn mixes text and images.</summary>
public abstract record AiContentPart;

public sealed record AiTextPart(string Text) : AiContentPart;

/// <summary>
/// An image referenced by URL. Remote <c>https</c> URLs are preferred over <c>data:</c> URIs — the
/// provider fetches them itself, which avoids base64-inflating every request by ~33%.
/// </summary>
public sealed record AiImagePart(string Url) : AiContentPart;

/// <summary>
/// One call the model asked for, as the assistant message that asked for it carries it when the
/// conversation is sent back with the results.
/// </summary>
public sealed record AiToolCallPart(AiToolCall Call) : AiContentPart;

/// <summary>What one tool call returned, sent in a <see cref="AiChatRole.Tool"/> message of its own.</summary>
/// <param name="ToolCallId">The <see cref="AiToolCall.Id"/> it answers.</param>
public sealed record AiToolResultPart(string ToolCallId, string Content) : AiContentPart;

/// <param name="Id">The provider's id for the call, which its result has to name.</param>
/// <param name="ArgumentsJson">The arguments as the model wrote them: a JSON object, not yet validated.</param>
public sealed record AiToolCall(string Id, string Name, string ArgumentsJson);

/// <summary>A tool the model may call.</summary>
/// <param name="Description">When to use it and when not: the model reads nothing else about it.</param>
/// <param name="ParametersSchemaJson">Its arguments as a JSON schema object.</param>
public sealed record AiToolDefinition(string Name, string Description, string ParametersSchemaJson);

/// <summary>
/// Whether the model may call the tools a request declares. There is no "required": not every free
/// model accepts it (dots-3-note-preview takes only auto and none, 2026-10-23), and the excerpts sent
/// with the question often answer it without any call.
/// </summary>
public enum AiToolChoice
{
    /// <summary>The model decides.</summary>
    Auto = 0,

    /// <summary>
    /// The model must answer in text. The tools stay declared, because a conversation that already
    /// holds tool calls and results is one some providers refuse once no tools are declared.
    /// </summary>
    None
}

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

    /// <summary>
    /// The model's turn that asked for tools, sent back ahead of their results. Any text it wrote
    /// beside the calls is kept, but whitespace alone is not: dots-3-note-preview writes "\n\n\n" there.
    /// </summary>
    public static AiChatMessage AssistantToolCalls(string? text, IEnumerable<AiToolCall> calls)
    {
        var parts = new List<AiContentPart>();
        if (!string.IsNullOrWhiteSpace(text))
        {
            parts.Add(new AiTextPart(text));
        }

        parts.AddRange(calls.Select(call => new AiToolCallPart(call)));
        return new AiChatMessage(AiChatRole.Assistant, parts);
    }

    public static AiChatMessage ToolResult(string toolCallId, string content) =>
        new(AiChatRole.Tool, [new AiToolResultPart(toolCallId, content)]);

    /// <summary>The tool calls this message carries, for an assistant turn that asked for some.</summary>
    public IEnumerable<AiToolCall> ToolCalls => Parts.OfType<AiToolCallPart>().Select(part => part.Call);

    /// <summary>
    /// Plain-text projection, used for length budgeting and logging. A tool result counts as text: it
    /// is what the model reads.
    /// </summary>
    public string ToPlainText() =>
        string.Join(" ", Parts.Select(part => part switch
        {
            AiTextPart text => text.Text,
            AiToolResultPart result => result.Content,
            _ => null
        }).OfType<string>());
}

/// <summary>
/// A completion request. <paramref name="ModelChain"/> is ordered: the first entry is the preferred
/// model and the rest are server-side fallbacks, so one HTTP call survives a model being down,
/// rate-limited, or refusing on moderation grounds.
/// </summary>
/// <param name="Tools">Tools the model may call; null or empty declares none.</param>
/// <param name="ToolChoice">Whether it may call them. Ignored when no tools are declared.</param>
public sealed record AiChatRequest(
    IReadOnlyList<string> ModelChain,
    IReadOnlyList<AiChatMessage> Messages,
    double? Temperature = null,
    int? MaxTokens = null,
    IReadOnlyList<AiToolDefinition>? Tools = null,
    AiToolChoice ToolChoice = AiToolChoice.Auto);

public sealed record AiTokenUsage(int PromptTokens, int CompletionTokens);

/// <summary>
/// <paramref name="ModelUsed"/> is the model that actually answered, which may be any entry from the
/// chain. Recording it is what makes "the bot got worse today" diagnosable.
/// </summary>
/// <param name="Text">
/// The answer. Empty, or whitespace, when the model asked for tools instead — a reply that is only
/// tool calls carries no content, and is a success all the same.
/// </param>
/// <param name="ToolCalls">The tools the model asked to call, in its order; null or empty for an answer.</param>
/// <param name="FinishReason">Why the model stopped, as the provider put it ("stop", "tool_calls", "length").</param>
public sealed record AiChatResponse(
    string Text,
    string ModelUsed,
    AiTokenUsage Usage,
    IReadOnlyList<AiToolCall>? ToolCalls = null,
    string? FinishReason = null)
{
    /// <summary>The model asked for tools rather than answering.</summary>
    public bool RequestsTools => ToolCalls is { Count: > 0 };
}

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

    /// <summary>
    /// The chain's first model refuses to serve this bot at all — OpenRouter reserves some free models
    /// for agentic harnesses and answers anything else with 403. Unlike <see cref="Rejected"/> it is
    /// about the model, not the request: other models answer the same request, and this one never will.
    /// Raised only when the provider's message names that first model, so it is the one to blame.
    /// </summary>
    public const string ModelGated = "ai.model_gated";

    /// <summary>
    /// The chain's first model is out of capacity at its upstream provider — a pool shared by every
    /// free user, not our key's allowance. Unlike <see cref="RateLimited"/> it says nothing about our
    /// allowance or the other models, which are worth asking straight away. Raised only when the
    /// provider's message names that first model, or no model at all.
    /// </summary>
    public const string ModelRateLimited = "ai.model_rate_limited";

    /// <summary>The model answered with nothing.</summary>
    public const string EmptyResponse = "ai.empty_response";

    /// <summary>
    /// The provider refused the request because of the tools it declares — "No endpoints found that
    /// support tool use" — rather than anything about the question. Unlike <see cref="Rejected"/> the
    /// same question is worth asking again without them.
    /// </summary>
    public const string ToolsUnsupported = "ai.tools_unsupported";

    /// <summary>There was no model to ask.</summary>
    public const string NoModelAvailable = "ai.no_model_available";

    /// <summary>
    /// A completion arrived but is not an answer — a safety classifier's verdict, or a reply from a
    /// model that should never have been asked. Raised by the use case, not by the client.
    /// </summary>
    public const string UnusableAnswer = "ai.unusable_answer";

    /// <summary>
    /// A completion arrived but is a tool call written out as text — <c>&lt;tool_call&gt;</c> markup or
    /// a bare JSON call — rather than an answer or a real call. Unlike <see cref="UnusableAnswer"/> it
    /// says nothing against the model: nemotron-3-ultra wrote one when told to answer after its tool
    /// rounds, and answered the same question well once asked without tools. Raised by the use case.
    /// </summary>
    public const string ToolCallMarkup = "ai.tool_call_markup";
}
