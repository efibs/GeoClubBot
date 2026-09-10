using FluentAssertions;
using Polly.CircuitBreaker;
using Xunit;

namespace GeoClubBot.Tests.Api;

public sealed class ResilienceRejectionHandlerTests
{
    [Fact]
    public async Task ARequestThePipelineRefused_SurfacesAsAnHttpRequestException()
    {
        // The adapters behind these clients catch HttpRequestException and nothing else. A refusal that
        // escaped as Polly's own type aborted whatever called — for ingestion, the rest of the night's run.
        using var client = new HttpClient(new ResilienceRejectionHandler { InnerHandler = new RefusingHandler() });

        var act = () => client.GetAsync(new Uri("https://openrouter.ai/api/v1/embeddings"));

        (await act.Should().ThrowAsync<HttpRequestException>())
            .WithInnerException<BrokenCircuitException>();
    }

    private sealed class RefusingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new BrokenCircuitException("The circuit is now open.");
    }
}
