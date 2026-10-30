using System.Net;
using Infrastructure.OutputAdapters.AI.OpenRouter;

namespace GeoClubBot.RetrievalProbe;

/// <summary>
/// What the bot's OpenRouter resilience pipeline does for <c>answer</c>, which cannot use it: it lives in
/// the API project. Requests are paced to the key's per-minute budget, each attempt is bounded, and a
/// failure the pipeline would retry is retried the same way — a transient one twice with backoff, a 429
/// once after the minute it was refused in, unless it is one model's provider limit, which the answer's
/// own chain handles. Every attempt passes the guard beneath this handler, so every one is counted.
///
/// Without it a 504 the bot would have retried away fails the record, and a burst of parallel answers
/// runs into the provider's 20 requests a minute.
/// </summary>
public sealed class PacedRetryHandler(
    HttpMessageHandler innerHandler,
    int requestsPerMinute,
    TimeSpan attemptTimeout,
    TimeProvider? clock = null) : DelegatingHandler(innerHandler)
{
    /// <summary>As the bot's pipeline: two retries, the first after two seconds.</summary>
    public const int MaxRetries = 2;

    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(2);

    /// <summary>The longest a stated or computed rate-limit wait is honoured; past it the request fails.</summary>
    private static readonly TimeSpan MaxRateLimitWait = TimeSpan.FromSeconds(65);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Queue<DateTimeOffset> _sent = new();
    private readonly SemaphoreSlim _pace = new(1, 1);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Buffered once, so a retry can send the same body again.
        if (request.Content is not null)
        {
            await request.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
        }

        for (var attempt = 0; ; attempt++)
        {
            await WaitForSlotAsync(cancellationToken).ConfigureAwait(false);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(attemptTimeout);

            HttpResponseMessage response;
            try
            {
                response = await base.SendAsync(request, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException
                                              && !cancellationToken.IsCancellationRequested
                                              && attempt < MaxRetries)
            {
                var delay = Backoff(attempt);
                Console.Error.WriteLine(
                    $"OpenRouter: {request.Method} {request.RequestUri?.AbsolutePath} "
                    + $"{(exception is OperationCanceledException ? $"took over {attemptTimeout.TotalSeconds:0} s" : "failed")}; "
                    + $"retrying in {delay.TotalSeconds:0} s.");
                await Task.Delay(delay, _clock, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var isUpstreamLimit = response.StatusCode == HttpStatusCode.TooManyRequests
                                  && request.RequestUri?.AbsolutePath.EndsWith("/chat/completions", StringComparison.Ordinal) == true
                                  && OpenRouterFailureClassifier.IsUpstreamRateLimit(
                                      await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

            if (RetryDelay(response.StatusCode, attempt, isUpstreamLimit, response.Headers.RetryAfter?.Delta, _clock.GetUtcNow())
                is not { } wait)
            {
                return response;
            }

            Console.Error.WriteLine(
                $"OpenRouter: {request.Method} {request.RequestUri?.AbsolutePath} answered {(int)response.StatusCode}; "
                + $"retrying in {wait.TotalSeconds:0} s.");
            response.Dispose();
            await Task.Delay(wait, _clock, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// How long to wait before asking again after <paramref name="status"/>, or null to hand the response
    /// to the caller, as the bot's pipeline decides it.
    /// </summary>
    /// <param name="attempt">The attempt that got it, from zero.</param>
    /// <param name="isUpstreamLimit">A 429 naming one model's provider: the chain moves on rather than waits.</param>
    /// <param name="retryAfter">The wait the provider stated, if any.</param>
    public static TimeSpan? RetryDelay(HttpStatusCode status, int attempt, bool isUpstreamLimit, TimeSpan? retryAfter, DateTimeOffset now)
    {
        if (status == HttpStatusCode.TooManyRequests)
        {
            // The window turns over once. Still limited after that, something else spends the key.
            if (attempt > 0 || isUpstreamLimit)
            {
                return null;
            }

            var untilNextMinute = TimeSpan.FromSeconds(61 - now.Second);
            var wait = retryAfter ?? untilNextMinute;
            return wait <= MaxRateLimitWait ? (wait < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : wait) : null;
        }

        var transient = status is HttpStatusCode.RequestTimeout or >= HttpStatusCode.InternalServerError;
        return transient && attempt < MaxRetries ? Backoff(attempt) : null;
    }

    private static TimeSpan Backoff(int attempt) => BaseDelay * Math.Pow(2, attempt);

    /// <summary>At most the budget's requests in any sixty seconds: waits for the oldest to age out.</summary>
    private async Task WaitForSlotAsync(CancellationToken cancellationToken)
    {
        var budget = Math.Max(1, requestsPerMinute);
        while (true)
        {
            TimeSpan wait;
            await _pace.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var now = _clock.GetUtcNow();
                while (_sent.Count > 0 && now - _sent.Peek() >= TimeSpan.FromMinutes(1))
                {
                    _sent.Dequeue();
                }

                if (_sent.Count < budget)
                {
                    _sent.Enqueue(now);
                    return;
                }

                wait = _sent.Peek() + TimeSpan.FromMinutes(1) - now + TimeSpan.FromMilliseconds(50);
            }
            finally
            {
                _pace.Release();
            }

            await Task.Delay(wait, _clock, cancellationToken).ConfigureAwait(false);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pace.Dispose();
        }

        base.Dispose(disposing);
    }
}
