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
    /// Pipeline for the OpenRouter chat API. Unlike the GeoGuessr pipeline this is budgeted per
    /// <em>minute</em>, because the provider's free tier caps requests per minute rather than per
    /// second, and exceeding it costs the whole allowance rather than just slowing us down.
    /// </summary>
    /// <param name="requestsPerMinute">Should stay just under the provider's ceiling (20/min on the free tier).</param>
    public static void AddOpenRouterResiliencePipeline(
        ResiliencePipelineBuilder<HttpResponseMessage> builder,
        int requestsPerMinute)
    {
        // Waits rather than throwing, so a burst of Discord messages queues instead of failing.
        var rateLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = requestsPerMinute,
            TokensPerPeriod = requestsPerMinute,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            QueueLimit = 32,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });

        var retryStrategy = new HttpRetryStrategyOptions
        {
            // Deliberately low: the request already carries a server-side model fallback chain, so a
            // failure that reaches us has usually exhausted several models already.
            MaxRetryAttempts = 2,
            Delay = TimeSpan.FromSeconds(2),
            BackoffType = DelayBackoffType.Exponential,
            // Honour Retry-After on 429 instead of guessing; the provider states when the window resets.
            ShouldRetryAfterHeader = true
        };

        var circuitBreakerStrategy = new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            SamplingDuration = TimeSpan.FromSeconds(60),
            MinimumThroughput = 5,
            BreakDuration = TimeSpan.FromMinutes(2)
        };

        builder
            .AddRateLimiter(rateLimiter)
            .AddRetry(retryStrategy)
            .AddCircuitBreaker(circuitBreakerStrategy);
    }
}
