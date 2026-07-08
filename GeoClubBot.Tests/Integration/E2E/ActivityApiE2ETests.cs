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
using UseCases.OutputPorts.GeoGuessr;
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
    private readonly StubDirectMessageAccess _directMessages = new();
    private readonly StubChannelMessageAccess _channelMessages = new();

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

                // /me/activity and /club/todays-xp compute from the live GeoGuessr activities feed;
                // serve an empty feed instead of letting the test host call geoguessr.com.
                services.RemoveAll<IGeoGuessrActivityReader>();
                services.AddSingleton<IGeoGuessrActivityReader>(new StubGeoGuessrActivityReader());

                // Setting a reminder sends a confirmation DM and starting a link request posts an
                // admin heads-up; both would hit the (never-connected) gateway in the test host.
                services.RemoveAll<IDiscordDirectMessageAccess>();
                services.AddSingleton<IDiscordDirectMessageAccess>(_directMessages);
                services.RemoveAll<IDiscordMessageAccess>();
                services.AddSingleton<IDiscordMessageAccess>(_channelMessages);
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
    public async Task GET_me_requires_authentication()
    {
        var response = await _client.GetAsync("/api/v1/activity/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_me_returns_the_unlinked_shape()
    {
        var dto = await GetMeAsync();

        dto.DiscordUserId.Should().Be(_viewerDiscordId.ToString());
        // The real permission adapter answers false in the test host (the gateway never connects),
        // which is exactly the fail-closed default a regular viewer should get.
        dto.IsAdmin.Should().BeFalse();
        dto.Linked.Should().BeNull();
        dto.Club.Should().BeNull();
        dto.OpenLinkRequest.Should().BeNull();
    }

    [Fact]
    public async Task GET_me_returns_the_linked_account_and_club()
    {
        await SeedClubAsync(_mainClubId, "Main Club");
        await SeedMemberAsync(_viewerUserId, "ViewerNick", _viewerDiscordId, _mainClubId);

        var dto = await GetMeAsync();

        dto.Linked.Should().NotBeNull();
        dto.Linked!.GeoGuessrUserId.Should().Be(_viewerUserId);
        dto.Linked.Nickname.Should().Be("ViewerNick");
        dto.Club.Should().NotBeNull();
        dto.Club!.Name.Should().Be("Main Club");
    }

    [Fact]
    public async Task GET_me_includes_the_viewers_own_open_link_request_with_its_otp()
    {
        // The OTP is the secret the member sends to an admin via GeoGuessr DM; the owner may see
        // their own, so the frontend can re-display it.
        await SeedLinkRequestAsync(_viewerDiscordId, _viewerUserId, "otp-1234");

        var dto = await GetMeAsync();

        dto.Linked.Should().BeNull();
        dto.OpenLinkRequest.Should().NotBeNull();
        dto.OpenLinkRequest!.GeoGuessrUserId.Should().Be(_viewerUserId);
        dto.OpenLinkRequest.OneTimePassword.Should().Be("otp-1234");
    }

    [Fact]
    public async Task GET_me_activity_returns_not_found_when_unlinked()
    {
        var response = await _client.SendAsync(AuthorizedGet("/api/v1/activity/me/activity", ValidToken));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GET_me_activity_returns_the_viewers_own_window()
    {
        await SeedClubAsync(_mainClubId, "Main Club");
        await SeedMemberAsync(_viewerUserId, "ViewerNick", _viewerDiscordId, _mainClubId);

        var response = await _client.SendAsync(AuthorizedGet("/api/v1/activity/me/activity?daysBack=7", ValidToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<WeekActivityDto>();
        dto!.TotalXp.Should().Be(0);
        dto.Days.Should().NotBeNull();
    }

    [Fact]
    public async Task GET_me_profile_returns_not_found_when_unlinked()
    {
        var response = await _client.SendAsync(AuthorizedGet("/api/v1/activity/me/profile", ValidToken));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GET_missions_stats_scopes_to_the_viewers_club()
    {
        await SeedClubAsync(_mainClubId, "Main Club");
        await SeedMemberAsync(_viewerUserId, "ViewerNick", _viewerDiscordId, _mainClubId);

        var response = await _client.SendAsync(AuthorizedGet("/api/v1/activity/missions/stats?daysBack=14", ValidToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<MissionStatsDto>();
        dto!.ClubName.Should().Be("Main Club");
        dto.DaysWithMissionData.Should().Be(0);
    }

    [Fact]
    public async Task GET_missions_stats_falls_back_to_all_clubs_for_an_unlinked_viewer()
    {
        var response = await _client.SendAsync(AuthorizedGet("/api/v1/activity/missions/stats", ValidToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<MissionStatsDto>();
        dto!.ClubName.Should().BeNull();
    }

    [Fact]
    public async Task GET_club_todays_xp_returns_the_viewers_club()
    {
        await SeedClubAsync(_mainClubId, "Main Club");
        await SeedMemberAsync(_viewerUserId, "ViewerNick", _viewerDiscordId, _mainClubId);

        var response = await _client.SendAsync(AuthorizedGet("/api/v1/activity/club/todays-xp", ValidToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<TodaysXpDto>();
        dto!.ClubName.Should().Be("Main Club");
    }

    [Fact]
    public async Task Reminder_round_trip_works()
    {
        // No reminders yet.
        var reminders = await GetJsonAsync<List<ReminderDto>>("/api/v1/activity/me/reminders");
        reminders.Should().BeEmpty();

        // Add one (UTC, no time zone) — the confirmation DM is delivered by the stub.
        var postResponse = await SendJsonAsync(HttpMethod.Post, "/api/v1/activity/me/reminders",
            new AddReminderRequest("18:30", null, "Go play!"));
        postResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var added = await postResponse.Content.ReadFromJsonAsync<AddReminderResultDto>();
        added!.DmDelivered.Should().BeTrue();
        added.Reminder.TimeUtc.Should().Be("18:30");
        added.Reminder.CustomMessage.Should().Be("Go play!");
        _directMessages.SentMessages.Should().HaveCount(1);

        // Read it back, then remove it by id.
        reminders = await GetJsonAsync<List<ReminderDto>>("/api/v1/activity/me/reminders");
        reminders.Should().ContainSingle();

        var deleteResponse = await _client.SendAsync(
            Authorized(HttpMethod.Delete, $"/api/v1/activity/me/reminders/{added.Reminder.Id}"));
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        reminders = await GetJsonAsync<List<ReminderDto>>("/api/v1/activity/me/reminders");
        reminders.Should().BeEmpty();

        // Removing again reports the missing reminder.
        deleteResponse = await _client.SendAsync(
            Authorized(HttpMethod.Delete, $"/api/v1/activity/me/reminders/{added.Reminder.Id}"));
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Adding_multiple_reminders_keeps_them_all()
    {
        await SendJsonAsync(HttpMethod.Post, "/api/v1/activity/me/reminders",
            new AddReminderRequest("07:00", null, null));
        await SendJsonAsync(HttpMethod.Post, "/api/v1/activity/me/reminders",
            new AddReminderRequest("19:30", null, "evening"));

        var reminders = await GetJsonAsync<List<ReminderDto>>("/api/v1/activity/me/reminders");

        reminders.Should().HaveCount(2);
        reminders.Select(r => r.TimeUtc).Should().Equal("07:00", "19:30");
    }

    [Fact]
    public async Task POST_reminder_still_persists_when_the_confirmation_dm_fails()
    {
        _directMessages.DmsDisabled = true;

        var postResponse = await SendJsonAsync(HttpMethod.Post, "/api/v1/activity/me/reminders",
            new AddReminderRequest("07:00", null, null));

        postResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var added = await postResponse.Content.ReadFromJsonAsync<AddReminderResultDto>();
        added!.DmDelivered.Should().BeFalse();
        added.DmErrorCode.Should().Be("discord.dm.disabled");

        var reminders = await GetJsonAsync<List<ReminderDto>>("/api/v1/activity/me/reminders");
        reminders.Should().ContainSingle();
    }

    [Fact]
    public async Task POST_reminder_rejects_a_malformed_time()
    {
        var response = await SendJsonAsync(HttpMethod.Post, "/api/v1/activity/me/reminders",
            new AddReminderRequest("half past six", null, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_reminder_rejects_an_unknown_time_zone()
    {
        var response = await SendJsonAsync(HttpMethod.Post, "/api/v1/activity/me/reminders",
            new AddReminderRequest("18:30", "Mars/Olympus_Mons", null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Link_request_round_trip_works()
    {
        // Start: returns the OTP to its owner and posts the admin heads-up.
        var startResponse = await SendJsonAsync(HttpMethod.Post, "/api/v1/activity/me/link-request",
            new StartLinkRequest(_viewerUserId));
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var started = await startResponse.Content.ReadFromJsonAsync<LinkRequestDto>();
        started!.GeoGuessrUserId.Should().Be(_viewerUserId);
        started.OneTimePassword.Should().NotBeNullOrWhiteSpace();
        _channelMessages.SentMessages.Should().HaveCount(1);

        // /me now exposes the open request (owner-only OTP).
        var me = await GetMeAsync();
        me.OpenLinkRequest.Should().NotBeNull();
        me.OpenLinkRequest!.OneTimePassword.Should().Be(started.OneTimePassword);

        // A second request while one is open is rejected.
        var duplicateResponse = await SendJsonAsync(HttpMethod.Post, "/api/v1/activity/me/link-request",
            new StartLinkRequest(_viewerUserId));
        duplicateResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Cancel, then cancelling again 404s.
        var cancelResponse = await _client.SendAsync(Authorized(HttpMethod.Delete, "/api/v1/activity/me/link-request"));
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        cancelResponse = await _client.SendAsync(Authorized(HttpMethod.Delete, "/api/v1/activity/me/link-request"));
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task POST_link_request_conflicts_when_already_linked()
    {
        await SeedUserAsync(_viewerUserId, "ViewerNick", _viewerDiscordId);

        var response = await SendJsonAsync(HttpMethod.Post, "/api/v1/activity/me/link-request",
            new StartLinkRequest(Guid.NewGuid().ToString("N")[..24]));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task POST_link_request_conflicts_when_the_geoguessr_account_belongs_to_someone_else()
    {
        var otherDiscordId = (ulong)Random.Shared.NextInt64(1, long.MaxValue);
        await SeedUserAsync(_viewerUserId, "SomeoneElse", otherDiscordId);

        var response = await SendJsonAsync(HttpMethod.Post, "/api/v1/activity/me/link-request",
            new StartLinkRequest(_viewerUserId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
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

    private async Task<MeDto> GetMeAsync()
    {
        var response = await _client.SendAsync(AuthorizedGet("/api/v1/activity/me", ValidToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<MeDto>();
        dto.Should().NotBeNull();
        return dto!;
    }

    private static HttpRequestMessage AuthorizedGet(string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ValidToken);
        return request;
    }

    private async Task<T> GetJsonAsync<T>(string path)
    {
        var response = await _client.SendAsync(Authorized(HttpMethod.Get, path));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<T>();
        dto.Should().NotBeNull();
        return dto!;
    }

    private async Task<HttpResponseMessage> SendJsonAsync<T>(HttpMethod method, string path, T body)
    {
        var request = Authorized(method, path);
        request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
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

    private async Task SeedLinkRequestAsync(ulong discordUserId, string geoGuessrUserId, string oneTimePassword)
    {
        await using var db = _fixture.CreateDbContext();
        db.Add(GeoGuessrAccountLinkingRequest.Create(discordUserId, geoGuessrUserId, oneTimePassword));
        await db.SaveChangesAsync();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _baseFactory.DisposeAsync();
    }

    private sealed class StubDirectMessageAccess : IDiscordDirectMessageAccess
    {
        public bool DmsDisabled { get; set; }
        public List<string> SentMessages { get; } = [];

        public Task<Result> SendDirectMessageAsync(ulong discordUserId, string message, CancellationToken cancellationToken = default)
        {
            if (DmsDisabled)
            {
                return Task.FromResult<Result>(Error.Forbidden("discord.dm.disabled", "DMs disabled"));
            }
            SentMessages.Add(message);
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class StubChannelMessageAccess : IDiscordMessageAccess
    {
        public List<(string Message, ulong ChannelId)> SentMessages { get; } = [];

        public Task SendMessageAsync(string message, ulong channelId, CancellationToken cancellationToken = default)
        {
            SentMessages.Add((message, channelId));
            return Task.CompletedTask;
        }

        public Task SendSelfRolesMessageAsync(ulong channelId, IEnumerable<Entities.SelfRoleSetting> selfRoleSettings,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateSelfRolesMessageAsync(ulong channelId, ulong messageId,
            IEnumerable<Entities.SelfRoleSetting> selfRoleSettings, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteMessageAsync(ulong messageId, ulong channelId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StubGeoGuessrActivityReader : IGeoGuessrActivityReader
    {
        public Task<IReadOnlyList<ReadClubActivitiesItemDto>> ReadTodaysActivitiesAsync(
            Guid clubId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReadClubActivitiesItemDto>>([]);

        public Task<IReadOnlyList<ReadClubActivitiesItemDto>> ReadActivitiesSinceAsync(
            Guid clubId, DateTimeOffset since, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReadClubActivitiesItemDto>>([]);
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
