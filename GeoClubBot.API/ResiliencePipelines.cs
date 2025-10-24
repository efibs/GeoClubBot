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
}