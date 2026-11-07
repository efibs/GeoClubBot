using Configuration;

namespace Infrastructure.InputAdapters.GeoGuessrSocket;

/// <summary>The server refused the socket handshake with an HTTP status.</summary>
public sealed class SocketHandshakeException(int statusCode, TimeSpan? retryAfter, Exception? innerException = null)
    : Exception($"The socket handshake was refused with status {statusCode}.", innerException)
{
    public int StatusCode { get; } = statusCode;

    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// <summary>How a socket ended. A null code means there was no closing handshake.</summary>
public sealed record SocketClosure(int? Code, string? Description);

/// <summary>
/// One open socket to GeoGuessr. The seam that lets the supervisor be tested without a network, and
/// the mock GeoGuessr serve sockets in-process.
/// </summary>
public interface IGeoGuessrSocketSession : IAsyncDisposable
{
    Task SendAsync(string frame, CancellationToken cancellationToken);

    /// <summary>Yields text frames until the socket ends, which is then described by <see cref="Closure"/>.</summary>
    IAsyncEnumerable<string> ReadFramesAsync(CancellationToken cancellationToken);

    SocketClosure? Closure { get; }
}

/// <summary>Opens a socket as the account of one club's token.</summary>
public interface IGeoGuessrSocketConnector
{
    /// <param name="afterServerRestart">GeoGuessr closed the last connection for a restart (1012), which the site reports when reconnecting.</param>
    /// <exception cref="SocketHandshakeException">The server answered with something other than an upgrade.</exception>
    Task<IGeoGuessrSocketSession> ConnectAsync(GeoGuessrClubEntry club, bool afterServerRestart, CancellationToken cancellationToken);
}
