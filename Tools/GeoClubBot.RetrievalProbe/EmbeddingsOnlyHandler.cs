namespace GeoClubBot.RetrievalProbe;

/// <summary>
/// The only request the probe sends anywhere but the index is an embeddings request to OpenRouter,
/// and this makes sure of it: a chat completion, which is the bot's real cost, cannot leave the probe
/// even by mistake. It also counts what does leave, so every run can say what it spent from the
/// daily allowance the bot answers with.
///
/// The one exception is asked for by name. With <c>--allow-chat</c> the query planner's chat
/// completions may leave too, and the model roster it picks a model from may be read — and nothing
/// else, and only through the handler built for the planner.
/// </summary>
/// <param name="allowChat">Also lets chat completions and the model roster through.</param>
public sealed class EmbeddingsOnlyHandler(HttpMessageHandler innerHandler, bool allowChat = false)
    : DelegatingHandler(innerHandler)
{
    public const string EmbeddingsPath = "/api/v1/embeddings";

    public const string ChatCompletionsPath = "/api/v1/chat/completions";

    public const string ModelsPath = "/api/v1/models";

    private int _requestsSent;

    public int RequestsSent => _requestsSent;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Guard(request);
        Interlocked.Increment(ref _requestsSent);
        return base.SendAsync(request, cancellationToken);
    }

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Guard(request);
        Interlocked.Increment(ref _requestsSent);
        return base.Send(request, cancellationToken);
    }

    private void Guard(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath;
        var allowed = (request.Method == HttpMethod.Post && Is(path, EmbeddingsPath))
                      || (allowChat && request.Method == HttpMethod.Post && Is(path, ChatCompletionsPath))
                      || (allowChat && request.Method == HttpMethod.Get && Is(path, ModelsPath));

        if (!allowed)
        {
            throw new InvalidOperationException(
                $"GeoClubBot.RetrievalProbe only embeds{(allowChat ? " and plans" : string.Empty)}: "
                + $"refusing to send {request.Method} {request.RequestUri}.");
        }
    }

    private static bool Is(string? path, string expected) => string.Equals(path, expected, StringComparison.Ordinal);
}
