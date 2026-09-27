namespace GeoClubBot.RetrievalProbe;

/// <summary>
/// The only request the probe sends anywhere but the index is an embeddings request to OpenRouter,
/// and this makes sure of it: a chat completion, which is the bot's real cost, cannot leave the probe
/// even by mistake. It also counts what does leave, so every run can say what it spent from the
/// daily allowance the bot answers with.
/// </summary>
public sealed class EmbeddingsOnlyHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    public const string EmbeddingsPath = "/api/v1/embeddings";

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

    private static void Guard(HttpRequestMessage request)
    {
        if (request.Method != HttpMethod.Post
            || !string.Equals(request.RequestUri?.AbsolutePath, EmbeddingsPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"GeoClubBot.RetrievalProbe only embeds: refusing to send {request.Method} {request.RequestUri}.");
        }
    }
}
