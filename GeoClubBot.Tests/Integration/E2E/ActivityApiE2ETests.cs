using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Entities;
using FluentAssertions;
using GeoClubBot.DTOs;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UseCases.OutputPorts.Discord;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Integration.E2E;

/// <summary>
/// End-to-end coverage of the Club Dashboard Activity endpoints: the anonymous OAuth2 token
/// exchange and the bearer-gated aggregate dashboard. The Discord OAuth service is stubbed (no
/// outbound calls) and the activity is enabled via config; everything else travels the real
/// routing → auth handler → controller → MediatR → EF path against the shared Postgres container.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ActivityApiE2ETests : IAsyncLifetime
{
    private const ulong ViewerDiscordId = 999000111UL;
    private const string ValidToken = "valid-token";

    private readonly PostgresFixture _fixture;
    private readonly Guid _clubId = Guid.NewGuid();
    private readonly GeoClubBotApiFactory _baseFactory;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ActivityApiE2ETests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _baseFactory = new GeoClubBotApiFactory(fixture.ConnectionString, _clubId);
        _factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DiscordActivity:Enabled"] = "true"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDiscordOAuthService>();
                services.AddSingleton<IDiscordOAuthService>(new StubDiscordOAuthService(ViewerDiscordId, ValidToken));
            });
        });
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task GET_dashboard_requires_authentication()
    {
        var response = await _client.GetAsync("/api/v1/activity/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_dashboard_rejects_an_invalid_bearer_token()
    {
        var response = await _client.SendAsync(AuthorizedGet("/api/v1/activity/dashboard", "bogus"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_dashboard_returns_404_when_the_main_club_is_not_persisted()
    {
        var response = await _client.SendAsync(AuthorizedGet("/api/v1/activity/dashboard", ValidToken));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GET_dashboard_returns_the_club_and_resolves_the_viewer()
    {
        await SeedClubAsync();
        await SeedUserAsync("geo-1", "ViewerNick", ViewerDiscordId);

        var response = await _client.SendAsync(AuthorizedGet("/api/v1/activity/dashboard", ValidToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<DashboardDto>();
        dto.Should().NotBeNull();
        dto!.Club.Name.Should().Be("Activity E2E Club");
        dto.Viewer.Should().NotBeNull();
        dto.Viewer!.Nickname.Should().Be("ViewerNick");
        dto.Leaderboard.Should().BeEmpty();
        dto.Challenges.Should().BeEmpty();
        dto.Streaks.Should().BeEmpty();
    }

    [Fact]
    public async Task POST_token_exchanges_a_valid_code()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/activity/token", new ActivityTokenRequest("good-code"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<ActivityTokenResponse>();
        dto!.AccessToken.Should().Be("stub-access-token");
    }

    [Fact]
    public async Task POST_token_rejects_an_empty_code()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/activity/token", new ActivityTokenRequest(string.Empty));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static HttpRequestMessage AuthorizedGet(string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task SeedClubAsync()
    {
        await using var db = _fixture.CreateDbContext();
        db.Add(Club.Create(_clubId, "Activity E2E Club", level: 3));
        await db.SaveChangesAsync();
    }

    private async Task SeedUserAsync(string userId, string nickname, ulong discordUserId)
    {
        await using var db = _fixture.CreateDbContext();
        db.Add(GeoGuessrUser.Create(userId, nickname, discordUserId));
        await db.SaveChangesAsync();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _baseFactory.DisposeAsync();
    }

    private sealed class StubDiscordOAuthService(ulong userId, string validToken) : IDiscordOAuthService
    {
        public Task<Result<string>> ExchangeCodeForTokenAsync(string code, CancellationToken cancellationToken = default) =>
            Task.FromResult(code == "good-code"
                ? Result<string>.Success("stub-access-token")
                : Result<string>.Failure(Error.Unauthorized("activity.bad_code", "bad code")));

        public Task<Result<ulong>> GetUserIdAsync(string accessToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(accessToken == validToken
                ? Result<ulong>.Success(userId)
                : Result<ulong>.Failure(Error.Unauthorized("activity.bad_token", "bad token")));
    }
}
