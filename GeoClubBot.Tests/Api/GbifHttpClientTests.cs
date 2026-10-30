using Configuration;
using FluentAssertions;
using GeoClubBot.DependencyInjection;
using Infrastructure.OutputAdapters.AI.Gbif;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly.Timeout;
using UseCases.OutputPorts.AI;
using Xunit;

namespace GeoClubBot.Tests.Api;

/// <summary>
/// Pins how the species database's client is wired, through the real registration and resilience
/// handler: whatever the pipeline gives up with, the reader reports it as unreachable rather than throwing.
/// </summary>
public sealed class GbifHttpClientTests
{
    [Fact]
    public async Task AnAttemptThatTimesOutEveryTime_IsReportedAsUnreachable_NotThrown()
    {
        // Two timed-out attempts end in Polly's TimeoutRejectedException, which the reader does not catch:
        // the client lacked the handler that turns it into an HttpRequestException on every other one.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AI:Tools:SpeciesRange:RequestTimeoutSeconds"] = "1" })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAiServicesIfConfigured(configuration);
        services.AddHttpClient(GbifSpeciesRangeReader.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => new TimingOutHandler());
        await using var provider = services.BuildServiceProvider();

        var reader = new GbifSpeciesRangeReader(
            provider.GetRequiredService<IHttpClientFactory>(),
            Options.Create(new AiToolsConfiguration { SpeciesRange = { BaseUrl = "https://gbif.test" } }),
            TimeProvider.System,
            NullLogger<GbifSpeciesRangeReader>.Instance);

        var range = await reader.ReadAsync(new SpeciesRangeQuery("Pinus kesiya", null));

        range.IsFailure.Should().BeTrue("the tool tells the model the lookup failed, and the answer goes on");
    }

    /// <summary>Fails each attempt the way the pipeline's timeout does.</summary>
    private sealed class TimingOutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new TimeoutRejectedException("The attempt timed out.");
    }
}
