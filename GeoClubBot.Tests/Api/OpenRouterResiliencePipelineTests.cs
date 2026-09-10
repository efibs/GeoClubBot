using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.RateLimiting;
using FluentAssertions;
using Polly;
using Polly.RateLimiting;
using Xunit;

namespace GeoClubBot.Tests.Api;

/// <summary>
/// Pins the OpenRouter pipeline to what production needed. Free models are limited per minute, and each
/// way this pipeline used to exceed that — retries that skipped the limiter, a bucket that let a burst
/// through twice, retries fired seconds into a minute-long window — earned the 429s that failed a night's
/// indexing. Driven with zero retry delays and a supplied limiter and clock, so nothing waits in real time.
/// </summary>
public sealed class OpenRouterResiliencePipelineTests
{
    /// <summary>Five past five in the morning, UTC — when the nightly run met its 429s.</summary>
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 5, 4, 42, TimeSpan.Zero);

    [Fact]
    public async Task EveryAttempt_WaitsForItsOwnToken_RetriesIncluded()
    {
        // One token and no queue: the first attempt spends it, so a retry that honours the limiter is
        // refused rather than sent. With the limiter outermost, the same setup made three upstream calls.
        using var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 1,
            TokensPerPeriod = 1,
            ReplenishmentPeriod = TimeSpan.FromHours(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });

        var pipeline = Build(limiter);
        var upstreamCalls = 0;

        var act = () => pipeline.ExecuteAsync(_ =>
        {
            upstreamCalls++;
            return ValueTask.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }).AsTask();

        await act.Should().ThrowAsync<RateLimiterRejectedException>();
        upstreamCalls.Should().Be(1, "the retry had to queue for a token like any other request");
    }

    [Fact]
    public async Task ARateLimitedRequest_IsRetriedOnce_AfterWaitingOutTheWindow()
    {
        // The window turns over once. A second 429 after waiting it out means another consumer is spending
        // the key, and a third attempt would stall the caller another minute to learn the same thing.
        using var limiter = Unlimited();
        var pipeline = Build(limiter);
        var upstreamCalls = 0;

        var response = await pipeline.ExecuteAsync(_ =>
        {
            upstreamCalls++;
            return ValueTask.FromResult(RateLimited(retryAfter: TimeSpan.Zero));
        });

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        upstreamCalls.Should().Be(2);
    }

    [Fact]
    public async Task RateLimits_DoNotOpenTheCircuit()
    {
        // A 429 means we were early, not that the provider is down. Counted as failures, a handful of them
        // opened the circuit and failed every call — questions included — for two minutes.
        using var limiter = Unlimited();
        var pipeline = Build(limiter);

        for (var call = 0; call < 10; call++)
        {
            await pipeline.ExecuteAsync(_ => ValueTask.FromResult(RateLimited(retryAfter: TimeSpan.Zero)));
        }

        var afterwards = await pipeline.ExecuteAsync(_ => ValueTask.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        afterwards.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void RetryDelay_WaitsForTheResetTheProviderStates()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.Add("X-RateLimit-Reset",
            Now.AddSeconds(30).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));

        ResiliencePipelines.ComputeRetryDelay(response, Now)
            .Should().Be(TimeSpan.FromSeconds(31), "the stated reset, plus a second so the retry lands after it");
    }

    [Fact]
    public void RetryDelay_WaitsForTheNextMinute_WhenNoResetIsStated()
    {
        // Retried at 2 s and 4 s, both attempts landed inside the window that had refused the first — the
        // three 429s in six seconds production logged. OpenRouter's window turns over on the minute.
        ResiliencePipelines.ComputeRetryDelay(new HttpResponseMessage(HttpStatusCode.TooManyRequests), Now)
            .Should().Be(TimeSpan.FromSeconds(19), "05:04:42 is 18 seconds before the minute turns, plus a second");
    }

    [Fact]
    public void RetryDelay_HonoursRetryAfter()
    {
        var response = RateLimited(retryAfter: TimeSpan.FromSeconds(7));

        ResiliencePipelines.ComputeRetryDelay(response, Now).Should().Be(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public void RetryDelay_NeverOutwaitsTheCap()
    {
        var response = RateLimited(retryAfter: TimeSpan.FromMinutes(10));

        ResiliencePipelines.ComputeRetryDelay(response, Now).Should().Be(ResiliencePipelines.MaxRateLimitWait);
    }

    [Fact]
    public void RetryDelay_LeavesAnOrdinaryFailureToTheBackoff()
    {
        ResiliencePipelines.ComputeRetryDelay(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable), Now)
            .Should().BeNull();
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(18)]
    [InlineData(20)]
    [InlineData(60)]
    public void Limiter_CannotReleaseMoreThanItsBudget_InAnyMinute(int budget)
    {
        // The worst minute: the bucket starts full and every token added during the minute is spent the
        // moment it arrives. The old refill-everything-at-once bucket allowed twice the budget — measured,
        // 37 requests through a bucket of 18.
        var options = ResiliencePipelines.CreateOpenRouterRateLimiterOptions(budget);

        var ticksInAMinute = (int)Math.Ceiling(TimeSpan.FromMinutes(1) / options.ReplenishmentPeriod);
        var worstMinute = options.TokenLimit + ticksInAMinute * options.TokensPerPeriod;

        worstMinute.Should().BeLessThanOrEqualTo(budget);
    }

    [Fact]
    public void Limiter_LetsAQuestionsTwoRequestsThroughTogether()
    {
        // An embedding and then a completion. Pacing those apart would add seconds to every answer.
        ResiliencePipelines.CreateOpenRouterRateLimiterOptions(18).TokenLimit.Should().Be(2);
    }

    private static ResiliencePipeline<HttpResponseMessage> Build(RateLimiter limiter)
    {
        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>();
        ResiliencePipelines.ConfigureOpenRouterPipeline(
            builder, limiter, TimeSpan.FromSeconds(30), retryBaseDelay: TimeSpan.Zero, new FixedClock(Now));

        return builder.Build();
    }

    private static ConcurrencyLimiter Unlimited() =>
        new(new ConcurrencyLimiterOptions { PermitLimit = 1000, QueueLimit = 0 });

    private static HttpResponseMessage RateLimited(TimeSpan retryAfter)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter);
        return response;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
