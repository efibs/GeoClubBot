using FluentAssertions;
using Infrastructure.OutputAdapters.Http;
using Polly;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;
using Xunit;

namespace GeoClubBot.Tests.Api;

public sealed class ResilienceRejectionHandlerTests
{
    [Fact]
    public async Task ARequestThePipelineRefused_SurfacesAsAnHttpRequestException()
    {
        // The adapters behind these clients catch HttpRequestException and nothing else. A refusal that
        // escaped as Polly's own type aborted whatever called — for ingestion, the rest of the night's run.
        using var client = new HttpClient(new ResilienceRejectionHandler
        {
            InnerHandler = new RefusingHandler(new BrokenCircuitException("The circuit is now open."))
        });

        var act = () => client.GetAsync(new Uri("https://openrouter.ai/api/v1/embeddings"));

        (await act.Should().ThrowAsync<HttpRequestException>())
            .WithInnerException<BrokenCircuitException>();
    }

    public static TheoryData<ExecutionRejectedException> HostRefusals() =>
    [
        new BrokenCircuitException("The circuit is now open."),
        new IsolatedCircuitException("The circuit is manually held open."),
        new RateLimiterRejectedException("The rate-limit queue is full.")
    ];

    [Theory]
    [MemberData(nameof(HostRefusals))]
    public async Task ARefusalOnTheHostsAccount_IsReportedAsTheHostRefusing(ExecutionRejectedException refusal)
    {
        // Says nothing about the guide that was asked for. Recorded as that guide's failure, one host that
        // tripped the shared breaker backed off every source left in the run, other sites' included.
        using var client = new HttpClient(new ResilienceRejectionHandler { InnerHandler = new RefusingHandler(refusal) });

        var act = () => client.GetAsync(new Uri("https://docs.google.com/document/d/x/export?format=zip"));

        await act.Should().ThrowAsync<HostRefusedRequestException>();
    }

    [Fact]
    public async Task AnAttemptThatTimedOut_IsAnOrdinaryFailure_NotTheHostRefusing()
    {
        // A timeout is about the moment and is retried with backoff like any transient failure.
        using var client = new HttpClient(new ResilienceRejectionHandler
        {
            InnerHandler = new RefusingHandler(new TimeoutRejectedException("The attempt timed out."))
        });

        var act = () => client.GetAsync(new Uri("https://docs.google.com/document/d/x/export?format=zip"));

        var thrown = await act.Should().ThrowAsync<HttpRequestException>();
        thrown.Which.Should().NotBeOfType<HostRefusedRequestException>();
    }

    private sealed class RefusingHandler(Exception refusal) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw refusal;
    }
}
