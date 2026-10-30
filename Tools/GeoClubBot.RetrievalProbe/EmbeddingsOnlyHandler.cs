namespace GeoClubBot.RetrievalProbe;

/// <summary>
/// The only request the probe sends anywhere but the index is an embeddings request to OpenRouter,
/// and this makes sure of it: a chat completion, which is the bot's real cost, cannot leave the probe
/// even by mistake. It also counts what does leave, so every run can say what it spent from the
/// daily allowance the bot answers with.
///
/// The one exception is asked for by name. With <c>--allow-chat</c> chat completions may leave too,
/// and the two reads the bot picks a model with — the free-model roster and a model's provider health
/// (<c>GET /api/v1/models/{author}/{slug}/endpoints</c>) — and nothing else, and only through the
/// handlers built for the planner and for <c>answer</c>. Nothing that changes anything on the account
/// (keys, credits, settings) is on the list, so it cannot be reached whatever the bot's code asks for.
/// </summary>
/// <param name="allowChat">Also lets chat completions, the model roster and model health reads through.</param>
public sealed class EmbeddingsOnlyHandler(HttpMessageHandler innerHandler, bool allowChat = false)
    : DelegatingHandler(innerHandler)
{
    public const string EmbeddingsPath = "/api/v1/embeddings";

    public const string ChatCompletionsPath = "/api/v1/chat/completions";

    public const string ModelsPath = "/api/v1/models";

    private const string EndpointsSuffix = "/endpoints";

    private int _requestsSent;

    public int RequestsSent => _requestsSent;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Count(Guard(request));
        return base.SendAsync(request, cancellationToken);
    }

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Count(Guard(request));
        return base.Send(request, cancellationToken);
    }

    /// <summary>
    /// Whether <paramref name="path"/> is one model's provider health: exactly
    /// <c>/api/v1/models/{author}/{slug}/endpoints</c>, two plain segments between the two, so no other
    /// route under the models path qualifies by ending the same way.
    /// </summary>
    public static bool IsModelEndpointsPath(string? path)
    {
        if (path is null
            || !path.StartsWith(ModelsPath + "/", StringComparison.Ordinal)
            || !path.EndsWith(EndpointsSuffix, StringComparison.Ordinal)
            || path.Length <= ModelsPath.Length + 1 + EndpointsSuffix.Length)
        {
            return false;
        }

        var segments = path[(ModelsPath.Length + 1)..^EndpointsSuffix.Length].Split('/');
        return segments.Length == 2 && segments.All(IsPlainSegment);
    }

    private void Count(OpenRouterRequestKind kind)
    {
        Interlocked.Increment(ref _requestsSent);
        RequestAttribution.Current?.Count(kind);
    }

    private OpenRouterRequestKind Guard(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath;
        if (request.Method == HttpMethod.Post && Is(path, EmbeddingsPath))
        {
            return OpenRouterRequestKind.Embedding;
        }

        if (allowChat && request.Method == HttpMethod.Post && Is(path, ChatCompletionsPath))
        {
            return OpenRouterRequestKind.Chat;
        }

        if (allowChat && request.Method == HttpMethod.Get && (Is(path, ModelsPath) || IsModelEndpointsPath(path)))
        {
            return OpenRouterRequestKind.Roster;
        }

        throw new InvalidOperationException(
            $"GeoClubBot.RetrievalProbe only embeds{(allowChat ? ", answers and reads the model roster" : string.Empty)}: "
            + $"refusing to send {request.Method} {request.RequestUri}.");
    }

    /// <summary>
    /// One segment of a model id. A free model's slug ends in ":free", which a client may send
    /// percent-encoded; nothing else is let through encoded, so an encoded slash or dot cannot smuggle in
    /// another segment.
    /// </summary>
    private static bool IsPlainSegment(string segment)
    {
        var decoded = segment.Replace("%3A", ":", StringComparison.OrdinalIgnoreCase);
        return decoded.Length > 0 && decoded is not ("." or "..") && !decoded.Contains('%', StringComparison.Ordinal);
    }

    private static bool Is(string? path, string expected) => string.Equals(path, expected, StringComparison.Ordinal);
}

/// <summary>What a request to OpenRouter was for, as counted against the record it was made for.</summary>
public enum OpenRouterRequestKind
{
    Embedding,
    Chat,

    /// <summary>The model roster and provider health: free reads that pick a model, not answers.</summary>
    Roster
}

/// <summary>
/// The requests made for one rated answer, counted by the guard as they leave. Held in an async-local, so
/// answers replayed side by side each count their own, however their calls interleave.
/// </summary>
public sealed class RequestAttribution
{
    private static readonly AsyncLocal<RequestAttribution?> CurrentAttribution = new();

    private int _embeddings;
    private int _chats;
    private int _roster;

    /// <summary>The record whose answer the calling code is working on, or null outside one.</summary>
    public static RequestAttribution? Current
    {
        get => CurrentAttribution.Value;
        set => CurrentAttribution.Value = value;
    }

    public int Embeddings => _embeddings;

    public int Chats => _chats;

    public int Roster => _roster;

    public void Count(OpenRouterRequestKind kind)
    {
        switch (kind)
        {
            case OpenRouterRequestKind.Embedding:
                Interlocked.Increment(ref _embeddings);
                break;
            case OpenRouterRequestKind.Chat:
                Interlocked.Increment(ref _chats);
                break;
            default:
                Interlocked.Increment(ref _roster);
                break;
        }
    }
}
