using Infrastructure.OutputAdapters.Http;
using Polly;
using Polly.CircuitBreaker;
using Polly.RateLimiting;

namespace GeoClubBot;

/// <summary>
/// Reports a request the resilience pipeline refused to send as an <see cref="HttpRequestException"/>.
///
/// An open circuit, a full rate-limit queue and an attempt that timed out on every retry all surface
/// from Polly as its own exception types. The guide extractors and the image relay call HttpClient
/// directly and catch <see cref="HttpRequestException"/> — "the far side could not be reached" — and
/// nothing else, so a refusal escaped them and aborted whatever had called: one tripped circuit on a
/// guide host would end a nightly ingestion run and discard the bookkeeping of every source it had
/// already processed. (Refit clients receive it wrapped in Refit's own request exception, which their
/// adapters catch.) Translating at the client boundary keeps the adapters ignorant of the pipeline.
///
/// An open circuit or a full queue is about the host, not the request, and is reported as a
/// <see cref="HostRefusedRequestException"/> so ingestion can leave the guide alone instead of backing it
/// off; a timeout is about the moment and stays a plain failure.
/// </summary>
internal sealed class ResilienceRejectionHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (ExecutionRejectedException ex) when (ex is BrokenCircuitException or RateLimiterRejectedException)
        {
            throw new HostRefusedRequestException(
                $"The request to {request.RequestUri?.Host} was not sent: {ex.Message}", ex);
        }
        catch (ExecutionRejectedException ex)
        {
            throw new HttpRequestException(
                $"The request to {request.RequestUri?.Host} was not sent: {ex.Message}", ex);
        }
    }
}
