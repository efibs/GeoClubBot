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
///
/// The dashboard is personalized: it shows the viewing member's own club (resolved via their linked
/// GeoGuessr account → club membership), and no club data at all when the viewer can't be tied to a
/// club. Each test uses a unique viewer + club so they don't collide in the shared container.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ActivityApiE2ETests : IAsyncLifetime
{
    private const string ValidToken = "valid-token";
    private const string TestClientId = "test-discord-client-id";

    private readonly PostgresFixture _fixture;
    private readonly Guid _mainClubId = Guid.NewGuid();
    private readonly ulong _viewerDiscordId = (ulong)Random.Shared.NextInt64(1, long.MaxValue);
    // GeoGuessr user ids are varchar(24); take a unique 24-char slice so tests don't collide in the
    // shared container and the value still fits the column.
    private readonly string _viewerUserId = Guid.NewGuid().ToString("N")[..24];
    private readonly GeoClubBotApiFactory _baseFactory;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ActivityApiE2ETests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _baseFactory = new GeoClubBotApiFactory(fixture.ConnectionString, _mainClubId);
        _factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DiscordActivity:Enabled"] = "true",
                    ["DiscordActivity:ClientId"] = TestClientId
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDiscordOAuthService>();
                services.AddSingleton<IDiscordOAuthService>(new StubDiscordOAuthService(_viewerDiscordId, ValidToken));
            });
        });
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task GET_config_returns_the_public_discord_client_id()
    {
        // Anonymous: the frontend fetches the (public) client id at runtime before the OAuth handshake,
        // so the shipped bundle isn't tied to one Discord application.
        var response = await _client.GetAsync("/api/v1/activity/config");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<ActivityConfigDto>();
        dto!.ClientId.Should().Be(TestClientId);
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
    public async Task GET_dashboard_returns_no_club_for_an_unlinked_viewer()
    {
        // The Discord identity authenticates, but no GeoGuessr account is linked to it.
        await SeedClubAsync(_mainClubId, "Main Club");

        var dto = await GetDashboardAsync();

        dto.Club.Should().BeNull();
        dto.Viewer.Should().BeNull();
        dto.Leaderboard.Should().BeEmpty();
        dto.Challenges.Should().BeEmpty();
        dto.Streaks.Should().BeEmpty();
    }

    [Fact]
    public async Task GET_dashboard_returns_no_club_but_keeps_the_viewer_when_linked_without_membership()
    {
        await SeedClubAsync(_mainClubId, "Main Club");
        await SeedUserAsync(_viewerUserId, "ViewerNick", _viewerDiscordId);

        var dto = await GetDashboardAsync();

        // No club to show, but the linked viewer is still resolved so they can be highlighted in the
        // club-independent challenge standings.
        dto.Club.Should().BeNull();
        dto.Viewer.Should().NotBeNull();
        dto.Viewer!.Nickname.Should().Be("ViewerNick");
        dto.Leaderboard.Should().BeEmpty();
        dto.Streaks.Should().BeEmpty();
    }

    [Fact]
    public async Task GET_dashboard_returns_the_viewers_club_and_resolves_the_viewer()
    {
        await SeedClubAsync(_mainClubId, "Main Club");
        await SeedMemberAsync(_viewerUserId, "ViewerNick", _viewerDiscordId, _mainClubId);

        var dto = await GetDashboardAsync();

        dto.Club.Should().NotBeNull();
        dto.Club!.Name.Should().Be("Main Club");
        dto.Viewer.Should().NotBeNull();
        dto.Viewer!.Nickname.Should().Be("ViewerNick");
        dto.Leaderboard.Should().BeEmpty();
        dto.Challenges.Should().BeEmpty();
        dto.Streaks.Should().BeEmpty();
    }

    [Fact]
    public async Task GET_dashboard_shows_the_viewers_own_club_rather_than_the_main_club()
    {
        var otherClubId = Guid.NewGuid();
        await SeedClubAsync(_mainClubId, "Main Club");
        await SeedClubAsync(otherClubId, "Other Club");
        await SeedMemberAsync(_viewerUserId, "ViewerNick", _viewerDiscordId, otherClubId);

        var dto = await GetDashboardAsync();

        dto.Club.Should().NotBeNull();
        dto.Club!.Name.Should().Be("Other Club");
        dto.Viewer!.Nickname.Should().Be("ViewerNick");
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

    private async Task<DashboardDto> GetDashboardAsync()
    {
        var response = await _client.SendAsync(AuthorizedGet("/api/v1/activity/dashboard", ValidToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<DashboardDto>();
        dto.Should().NotBeNull();
        return dto!;
    }

    private static HttpRequestMessage AuthorizedGet(string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task SeedClubAsync(Guid clubId, string name)
    {
        await using var db = _fixture.CreateDbContext();
        db.Add(Club.Create(clubId, name, level: 3));
        await db.SaveChangesAsync();
    }

    private async Task SeedUserAsync(string userId, string nickname, ulong discordUserId)
    {
        await using var db = _fixture.CreateDbContext();
        db.Add(GeoGuessrUser.Create(userId, nickname, discordUserId));
        await db.SaveChangesAsync();
    }

    private async Task SeedMemberAsync(string userId, string nickname, ulong discordUserId, Guid clubId)
    {
        await using var db = _fixture.CreateDbContext();
        var user = GeoGuessrUser.Create(userId, nickname, discordUserId);
        db.Add(ClubMember.Create(user, clubId, xp: 0, joinedAt: DateTimeOffset.UtcNow));
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
