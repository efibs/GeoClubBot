using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UseCases.OutputPorts.AI;
using Xunit;

namespace GeoClubBot.Tests.Integration.E2E;

/// <summary>
/// The relay's public surface, end to end: a URL the relay hands out — and the same URL moved onto
/// today's public base — must be one the API actually serves, and <c>/health</c> must be where the
/// reachability check in <c>/ai status</c> looks. The route is a shared constant now, so these pin
/// that the relay, the answer path and the controller still agree on it.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AiImageApiE2ETests : IAsyncLifetime
{
    private const string PublicBaseUrl = "https://bot.example.net";

    /// <summary>A one-pixel PNG header: real magic bytes, so the relay's type sniffing accepts it.</summary>
    private static readonly byte[] PngBytes =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
        0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01
    ];

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"relay-e2e-{Guid.NewGuid():N}");
    private readonly GeoClubBotApiFactory _baseFactory;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public AiImageApiE2ETests(PostgresFixture fixture)
    {
        _baseFactory = new GeoClubBotApiFactory(fixture.ConnectionString, Guid.NewGuid());
        _factory = _baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AI:ImageRelay:PublicBaseUrl"] = PublicBaseUrl,
                    ["AI:ImageRelay:Directory"] = _directory
                })));
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task GET_serves_a_stored_image_at_the_url_the_relay_handed_out()
    {
        var stored = await StoreAsync();

        var response = await _client.GetAsync(new Uri(stored).AbsolutePath);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(PngBytes);
    }

    [Fact]
    public async Task GET_serves_an_image_indexed_under_an_old_host_once_its_url_is_rebased()
    {
        // The answer path shows a missing-on-disk picture's link under today's base; for a file that is
        // on disk the same rebased path must lead to it.
        var stored = await StoreAsync();
        var fromOldHost = stored.Replace(PublicBaseUrl, "https://old-host.tailnet.ts.net", StringComparison.Ordinal);

        var rebased = RelayedImageUrl.Rebase(fromOldHost, PublicBaseUrl);

        rebased.Should().Be(stored);
        (await _client.GetAsync(new Uri(rebased).AbsolutePath)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GET_answers_not_found_for_a_name_that_is_not_on_disk()
    {
        var response = await _client.GetAsync($"/{RelayedImageUrl.RoutePrefix}/{new string('f', 64)}.png");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GET_health_answers_where_the_reachability_check_looks()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<string> StoreAsync()
    {
        var relay = _factory.Services.GetRequiredService<IImageRelay>();
        var stored = await relay.StoreAsync(PngBytes, "image/png");
        stored.IsSuccess.Should().BeTrue();
        return stored.Value;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _baseFactory.DisposeAsync();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
