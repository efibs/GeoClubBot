namespace GeoClubBot.ApiProbe;

/// <summary>
/// Hard stop against ever mutating the account the probe is authenticated as. Every request the
/// probe makes passes through here, and anything that is not a GET or HEAD throws before it
/// reaches the network.
///
/// This is the innermost of three layers (see README.md): the project also references no other
/// project in the solution, so no write-capable client type is even reachable from here, and
/// every command is a hand-written GET.
/// </summary>
public sealed class ReadOnlyGuardHandler : DelegatingHandler
{
    public ReadOnlyGuardHandler(HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Guard(request);
        return base.SendAsync(request, cancellationToken);
    }

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Guard(request);
        return base.Send(request, cancellationToken);
    }

    private static void Guard(HttpRequestMessage request)
    {
        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
        {
            throw new InvalidOperationException(
                $"GeoClubBot.ApiProbe is read-only: refusing to send {request.Method} {request.RequestUri}. "
                + "If you genuinely need a write, do it deliberately somewhere else - not in this tool.");
        }

        if (request.Content is not null)
        {
            throw new InvalidOperationException(
                "GeoClubBot.ApiProbe is read-only: refusing to send a request with a body.");
        }
    }
}
