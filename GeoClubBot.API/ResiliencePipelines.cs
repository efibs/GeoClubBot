using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace GeoClubBot;

internal static class ResiliencePipelines
{
    public static void AddGeoGuessrApiResiliencePipeline(ResiliencePipelineBuilder<HttpResponseMessage> builder)
    {
        // Configure a token bucket rate limiter that WAITS, not throws
        var rateLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 10,                   // up to 10 requests per second
            TokensPerPeriod = 10,              // refill 10 per period
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = 100,                  // allow waiting for up to 100 queued requests
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });

        // Define the retry strategy (exponential backoff)
        var retryStrategy = new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromSeconds(1),
            BackoffType = DelayBackoffType.Exponential
        };

        // Define the circuit breaker
        var circuitBreakerStrategy = new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,                    // trip if >=50% of samples fail
            SamplingDuration = TimeSpan.FromSeconds(30),
            MinimumThroughput = 10,                // need at least 10 samples before evaluation
            BreakDuration = TimeSpan.FromMinutes(5)
        };

        // Combine them into a pipeline
        builder
            .AddRateLimiter(rateLimiter)   // waits automatically if limit reached
            .AddRetry(retryStrategy)
            .AddCircuitBreaker(circuitBreakerStrategy);
    }

    /// <summary>
    /// Pipeline for fetching third-party guide content during ingestion.
    ///
    /// Deliberately slow. These are other people's sites being read in bulk by an unattended job, so
    /// the limiter is set for politeness rather than throughput; a nightly run has all night.
    /// </summary>
    public static void AddContentSourceResiliencePipeline(ResiliencePipelineBuilder<HttpResponseMessage> builder)
    {
        var rateLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 2,
            TokensPerPeriod = 2,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = 256,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });

        var retryStrategy = new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 2,
            Delay = TimeSpan.FromSeconds(3),
            BackoffType = DelayBackoffType.Exponential,
            ShouldRetryAfterHeader = true
        };

        var circuitBreakerStrategy = new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            SamplingDuration = TimeSpan.FromSeconds(60),
            MinimumThroughput = 10,
            BreakDuration = TimeSpan.FromMinutes(5)
        };

        builder
            .AddRateLimiter(rateLimiter)
            .AddRetry(retryStrategy)
            .AddCircuitBreaker(circuitBreakerStrategy);
    }

    /// <summary>
    /// Burst a caller may use before pacing starts: enough for a question's embedding and completion to
    /// go out together, so a lone question is never slowed by the limiter.
    /// </summary>
    private const int OpenRouterBurst = 2;

    /// <summary>Longest a request waits for the provider's rate-limit window to turn over before retrying.</summary>
    internal static readonly TimeSpan MaxRateLimitWait = TimeSpan.FromSeconds(65);

    /// <summary>Slack past the stated reset, so a retry cannot land a moment before the window turns over.</summary>
    private static readonly TimeSpan RateLimitResetSlack = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan OpenRouterRetryBaseDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Pipeline for the OpenRouter API, which caps free models per <em>minute</em> rather than per second.
    ///
    /// The order is the substance. Retry sits outside the rate limiter, so every attempt — retries
    /// included — waits for a token; with the limiter outermost, one token bought up to three upstream
    /// calls, and each retry was exactly the request that tipped the window over. The attempt timeout is
    /// innermost, so it measures the request rather than time spent queueing for a token or waiting out a
    /// 429. The whole call is bounded by the HttpClient's own timeout.
    /// </summary>
    /// <param name="requestsPerMinute">Should stay just under the provider's ceiling (20/min for free models).</param>
    public static void AddOpenRouterResiliencePipeline(
        ResiliencePipelineBuilder<HttpResponseMessage> builder,
        int requestsPerMinute,
        TimeSpan attemptTimeout) =>
        ConfigureOpenRouterPipeline(
            builder,
            new TokenBucketRateLimiter(CreateOpenRouterRateLimiterOptions(requestsPerMinute)),
            attemptTimeout,
            OpenRouterRetryBaseDelay,
            TimeProvider.System);

    /// <summary>The pipeline with its moving parts supplied, so tests can drive it without waiting in real time.</summary>
    internal static void ConfigureOpenRouterPipeline(
        ResiliencePipelineBuilder<HttpResponseMessage> builder,
        RateLimiter rateLimiter,
        TimeSpan attemptTimeout,
        TimeSpan retryBaseDelay,
        TimeProvider clock)
    {
        var retryStrategy = new HttpRetryStrategyOptions
        {
            // Deliberately low: a chat request already carries a server-side model fallback chain, so a
            // failure that reaches us has usually exhausted several models already.
            MaxRetryAttempts = 2,
            Delay = retryBaseDelay,
            BackoffType = DelayBackoffType.Exponential,
            ShouldHandle = args => ValueTask.FromResult(
                args.Outcome.Result?.StatusCode == HttpStatusCode.TooManyRequests
                    // The window turns over once. Still limited after waiting it out means something
                    // else is spending the same key, and waiting a second minute would only stall.
                    ? args.AttemptNumber == 0
                    : HttpClientResiliencePredicates.IsTransient(args.Outcome)),
            DelayGenerator = args => ValueTask.FromResult(
                args.Outcome.Result is { } response ? ComputeRetryDelay(response, clock.GetUtcNow()) : null)
        };

        var circuitBreakerStrategy = new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            SamplingDuration = TimeSpan.FromSeconds(60),
            MinimumThroughput = 5,
            BreakDuration = TimeSpan.FromMinutes(2),
            // A 429 means we were early, not that the provider is down, and the retry already waits it
            // out. Counting it would open the circuit and fail every call for two minutes over pacing.
            ShouldHandle = args => ValueTask.FromResult(
                args.Outcome.Result?.StatusCode != HttpStatusCode.TooManyRequests
                && HttpClientResiliencePredicates.IsTransient(args.Outcome))
        };

        builder
            .AddRetry(retryStrategy)
            .AddCircuitBreaker(circuitBreakerStrategy)
            .AddRateLimiter(rateLimiter)
            .AddTimeout(attemptTimeout);
    }

    /// <summary>
    /// Paces requests evenly rather than releasing a minute's allowance in one lump.
    ///
    /// A bucket refilled all at once lets a quiet spell followed by a burst straddle the refill and send
    /// twice the budget within seconds — measured at 37 through a bucket of 18 — which is how a limiter
    /// set below the provider's 20/min still earned 429s. This bucket holds only a small burst and gains
    /// one token at a fixed interval, so no 60-second span can carry more than the burst plus one
    /// minute's ticks, and the two together are the budget.
    /// </summary>
    internal static TokenBucketRateLimiterOptions CreateOpenRouterRateLimiterOptions(int requestsPerMinute)
    {
        var budget = Math.Max(1, requestsPerMinute);
        var burst = Math.Clamp(budget - 1, 1, OpenRouterBurst);
        var steady = Math.Max(1, budget - burst);

        return new TokenBucketRateLimiterOptions
        {
            TokenLimit = burst,
            TokensPerPeriod = 1,
            // Rounded up to whole milliseconds: a period shortened by rounding would fit one tick too many
            // into a minute.
            ReplenishmentPeriod = TimeSpan.FromMilliseconds(Math.Ceiling(60_000d / steady)),
            // Waits rather than failures: a queued question is slower, a rejected one is lost. Sized well
            // above the concurrent callers — the answer limiter plus one ingestion run.
            QueueLimit = 64,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        };
    }

    /// <summary>
    /// How long to wait before retrying <paramref name="response"/>, or null for the ordinary backoff.
    ///
    /// A per-minute window cannot be retried out of in seconds: backoff at 2 s and 4 s lands both retries
    /// inside the window that refused the first, which is what production logged — three 429s in six
    /// seconds, each counted against the window. So a rate-limited response waits for the reset the
    /// provider states, and without one for the next wall-clock minute, where OpenRouter's free-model
    /// window turns over.
    /// </summary>
    internal static TimeSpan? ComputeRetryDelay(HttpResponseMessage response, DateTimeOffset now)
    {
        if (response.Headers.RetryAfter is { } retryAfter && (retryAfter.Delta ?? retryAfter.Date - now) is { } stated)
        {
            return ClampRateLimitWait(stated);
        }

        if (response.StatusCode != HttpStatusCode.TooManyRequests)
        {
            return null;
        }

        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var values)
            && long.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var reset))
        {
            // OpenRouter states milliseconds; a value this small can only be seconds.
            var resetsAt = reset > 100_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds(reset)
                : DateTimeOffset.FromUnixTimeSeconds(reset);

            return ClampRateLimitWait(resetsAt - now + RateLimitResetSlack);
        }

        var intoMinute = TimeSpan.FromTicks(now.UtcTicks % TimeSpan.TicksPerMinute);
        return ClampRateLimitWait(TimeSpan.FromMinutes(1) - intoMinute + RateLimitResetSlack);
    }

    private static TimeSpan ClampRateLimitWait(TimeSpan wait) =>
        wait < TimeSpan.Zero ? TimeSpan.Zero : wait > MaxRateLimitWait ? MaxRateLimitWait : wait;
}
