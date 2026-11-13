namespace Infrastructure.OutputAdapters.Http;

/// <summary>
/// Reads each response to its end before handing it on, and gives up on one whose body does not arrive
/// within <paramref name="bodyTimeout"/> of its headers.
///
/// Refit asks for a response as soon as its headers are in, and reads the body afterwards. Everything
/// that bounds a request — the client's timeout, the resilience pipeline's attempt timeout — stops
/// counting at the headers, so nothing bounded the body. OpenRouter opens a chat completion's response
/// at once and keeps it open with blank padding while the model works: on 2026-11-11 nemotron-3-ultra
/// stopped answering mid-request and the response stayed open for 57 minutes, then ended empty. In the
/// bot that is a member watching "is typing" for an hour and one of the answer slots held all that time.
///
/// Registered outside the resilience pipeline, on purpose twice over: the body is read while the
/// client's overall timeout still counts, which that timeout's description always promised; and the
/// pipeline's retry never sees the failure, because sending the same request to the same stalled model
/// would spend the provider's daily requests on waiting again. The caller is told with
/// <see cref="ResponseBodyTimeoutException"/> and decides: the chat client reports it as the model's
/// failure, so the answer asks other models.
/// </summary>
/// <param name="bodyTimeout">Longest a body may take once its headers have arrived (<c>AI:ResponseTimeoutSeconds</c>).</param>
/// <param name="clock">Times the deadline; the system clock when null.</param>
public sealed class ResponseBodyDeadlineHandler(TimeSpan bodyTimeout, TimeProvider? clock = null) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        using var deadline = new CancellationTokenSource(bodyTimeout, clock ?? TimeProvider.System);
        using var either = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        try
        {
            await response.Content.LoadIntoBufferAsync(either.Token).ConfigureAwait(false);
            return response;
        }
        catch (Exception exception) when (IsDeadline(exception, deadline, cancellationToken))
        {
            // Disposing it closes the connection: nothing is left reading a body nobody waits for.
            response.Dispose();
            throw new ResponseBodyTimeoutException(
                $"{request.RequestUri?.Host} began to answer and had not finished after {bodyTimeout.TotalSeconds:0} s.", exception);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Whether the read ended because this handler's deadline passed, and not because the caller gave up
    /// or the client's own timeout fired — those stay what they were. A cancelled read can surface as
    /// the connection it closed, so a transport error after the deadline is the deadline too.
    /// </summary>
    private static bool IsDeadline(Exception exception, CancellationTokenSource deadline, CancellationToken cancellationToken) =>
        deadline.IsCancellationRequested
        && !cancellationToken.IsCancellationRequested
        && exception is OperationCanceledException or HttpRequestException or IOException;
}
