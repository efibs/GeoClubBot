using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;
using Xunit;

namespace GeoClubBot.Tests.Integration.E2E;

/// <summary>
/// End-to-end coverage of the club website's endpoint: a real request travels routing → rate
/// limiter → controller → MediatR → DTO → JSON. The two cached readers are stubbed, so neither
/// GeoGuessr nor Discord is called. The body is compared as a string because the website checks it
/// against a JSON schema that rejects any missing, extra or renamed key.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class StatsApiE2ETests : IAsyncLifetime
{
    private readonly Guid _mainClubId = Guid.NewGuid();
    private readonly Guid _secondClubId = Guid.NewGuid();
    private readonly IGeoGuessrClubReader _clubReader = Substitute.For<IGeoGuessrClubReader>();
    private readonly IDiscordOnlineCountReader _onlineCountReader = Substitute.For<IDiscordOnlineCountReader>();
    private readonly GeoClubBotApiFactory _baseFactory;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public StatsApiE2ETests(PostgresFixture fixture)
    {
        _baseFactory = new GeoClubBotApiFactory(fixture.ConnectionString, _mainClubId);
        _factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Website:Enabled"] = "true",
                    ["Website:SecondClubId"] = _secondClubId.ToString(),
                    ["GeoGuessr:Clubs:1:ClubId"] = _secondClubId.ToString(),
                    ["GeoGuessr:Clubs:1:IsMain"] = "false",
                    ["GeoGuessr:Clubs:1:NcfaToken"] = "test-ncfa-token"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IGeoGuessrClubReader>();
                services.AddSingleton(_clubReader);
                services.RemoveAll<IDiscordOnlineCountReader>();
                services.AddSingleton(_onlineCountReader);
            });
        });
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task GET_stats_returns_both_clubs_and_the_online_count()
    {
        // Read at different times and with fractions of a second: the section is dated by the
        // older read, in whole UTC seconds.
        ArrangeClub(_mainClubId, rank: 3, level: 36, members: 30, xp: 425770, totalClubs: 29168,
            readAt: new DateTimeOffset(2026, 10, 8, 9, 0, 12, 640, TimeSpan.Zero));
        ArrangeClub(_secondClubId, rank: 221, level: 20, members: 25, xp: 123400, totalClubs: 29168,
            readAt: new DateTimeOffset(2026, 10, 8, 9, 10, 0, TimeSpan.Zero));
        _onlineCountReader.ReadOnlineCountAsync(Arg.Any<CancellationToken>())
            .Returns(new DiscordOnlineCount(18, new DateTimeOffset(2026, 10, 8, 11, 12, 40, TimeSpan.FromHours(2))));

        var response = await _client.GetAsync("/api/v1/stats");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be(
            """{"geoguessr":{"updatedAt":"2026-10-08T09:00:12Z","totalClubs":29168,"clubs":{"main":{"rank":3,"level":36,"members":30,"xp":425770},"second":{"rank":221,"level":20,"members":25,"xp":123400}}},"discord":{"updatedAt":"2026-10-08T09:12:40Z","online":18}}""");
    }

    [Fact]
    public async Task GET_stats_sends_null_for_sources_that_could_not_be_read()
    {
        _clubReader.ReadClubAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((GeoGuessrClubSnapshot?)null);
        _onlineCountReader.ReadOnlineCountAsync(Arg.Any<CancellationToken>()).Returns((DiscordOnlineCount?)null);

        var response = await _client.GetAsync("/api/v1/stats");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("""{"geoguessr":null,"discord":null}""");
    }

    [Fact]
    public async Task GET_stats_allows_any_origin_and_short_caching()
    {
        _clubReader.ReadClubAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((GeoGuessrClubSnapshot?)null);
        _onlineCountReader.ReadOnlineCountAsync(Arg.Any<CancellationToken>()).Returns((DiscordOnlineCount?)null);

        // No Origin header: a CDN may fetch without one and serve the copy to browsers, so the
        // header must not depend on it.
        var response = await _client.GetAsync("/api/v1/stats");

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal("*");
        var cacheControl = response.Headers.CacheControl;
        cacheControl.Should().NotBeNull();
        cacheControl!.Public.Should().BeTrue();
        cacheControl.MaxAge.Should().Be(TimeSpan.FromSeconds(30));
        cacheControl.Extensions.Should().ContainSingle(e => e.Name == "stale-while-revalidate" && e.Value == "120");
    }

    [Fact]
    public async Task GET_stats_returns_404_when_the_website_is_disabled()
    {
        // appsettings.json ships the website disabled.
        using var client = _baseFactory.CreateClient();

        var response = await client.GetAsync("/api/v1/stats");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private void ArrangeClub(Guid clubId, int rank, int level, int members, int xp, int totalClubs, DateTimeOffset readAt) =>
        _clubReader.ReadClubAsync(clubId, Arg.Any<CancellationToken>())
            .Returns(new GeoGuessrClubSnapshot(clubId, level, members, xp, rank, totalClubs, readAt));

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _baseFactory.DisposeAsync();
    }
}
