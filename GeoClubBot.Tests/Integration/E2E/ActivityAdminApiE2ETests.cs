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
/// End-to-end coverage of the admin-only activity endpoints: the full authorization matrix
/// (anonymous → 401, authenticated non-admin → 403, admin → success) travelling the real
/// routing → authentication → policy → controller path. Discord is stubbed twice: the OAuth
/// service maps bearer tokens to Discord user ids, and the member-permission port decides who
/// counts as a guild administrator (the socket client never connects in the test host).
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ActivityAdminApiE2ETests : IAsyncLifetime
{
    private const string AdminToken = "admin-token";
    private const string MemberToken = "member-token";

    private readonly PostgresFixture _fixture;
    private readonly Guid _mainClubId = Guid.NewGuid();
    private readonly ulong _adminDiscordId = (ulong)Random.Shared.NextInt64(1, long.MaxValue);
    private readonly ulong _memberDiscordId = (ulong)Random.Shared.NextInt64(1, long.MaxValue);
    private readonly string _memberUserId = Guid.NewGuid().ToString("N")[..24];
    private readonly string _memberNickname = $"Member-{Guid.NewGuid():N}"[..20];
    private readonly GeoClubBotApiFactory _baseFactory;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ActivityAdminApiE2ETests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _baseFactory = new GeoClubBotApiFactory(fixture.ConnectionString, _mainClubId);
        _factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DiscordActivity:Enabled"] = "true",
                    ["DiscordActivity:ClientId"] = "test-discord-client-id"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDiscordOAuthService>();
                services.AddSingleton<IDiscordOAuthService>(new StubOAuthService(new Dictionary<string, ulong>
                {
                    [AdminToken] = _adminDiscordId,
                    [MemberToken] = _memberDiscordId
                }));

                services.RemoveAll<IDiscordMemberPermissionAccess>();
                services.AddSingleton<IDiscordMemberPermissionAccess>(new StubPermissionAccess(_adminDiscordId));

                // The per-member activity endpoint computes from the live GeoGuessr activities
                // feed; serve an empty feed instead of calling geoguessr.com.
                services.RemoveAll<IGeoGuessrActivityReader>();
                services.AddSingleton<IGeoGuessrActivityReader>(new StubGeoGuessrActivityReader());

                // Completing/cancelling a link (and the AccountLinked/Unlinked domain events)
                // assign roles and manage private channels via the gateway — no-op them here.
                services.RemoveAll<IDiscordServerRolesAccess>();
                services.AddSingleton<IDiscordServerRolesAccess>(new StubServerRolesAccess());
                services.RemoveAll<IDiscordTextChannelAccess>();
                services.AddSingleton<IDiscordTextChannelAccess>(new StubTextChannelAccess());
            });
        });
        _client = _factory.CreateClient();
    }

    /// <summary>Every admin read route — new ones must be added here so the matrix stays complete.</summary>
    public static TheoryData<string> AdminReadRoutes => new(
        "/api/v1/activity/admin/last-check-time",
        "/api/v1/activity/admin/strikes",
        "/api/v1/activity/admin/strikes/relevant",
        "/api/v1/activity/admin/members/SomeNickname/strikes",
        "/api/v1/activity/admin/excuses",
        "/api/v1/activity/admin/members/SomeNickname/activity",
        "/api/v1/activity/admin/members/SomeNickname/statistics",
        "/api/v1/activity/admin/club/statistics",
        "/api/v1/activity/admin/link-requests");

    [Theory]
    [MemberData(nameof(AdminReadRoutes))]
    public async Task Admin_endpoints_require_authentication(string route)
    {
        var response = await _client.GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(AdminReadRoutes))]
    public async Task Admin_endpoints_reject_an_authenticated_non_admin(string route)
    {
        // The decisive test for the whole admin area: a valid member token authenticates fine but
        // must be turned away by the policy with 403, never 200.
        var response = await _client.SendAsync(AuthorizedGet(route, MemberToken));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Every admin write route — the authorization matrix must cover mutations too.</summary>
    public static TheoryData<string, string> AdminWriteRoutes => new()
    {
        { "POST", "/api/v1/activity/admin/strikes" },
        { "POST", $"/api/v1/activity/admin/strikes/{Guid.Empty}/revoke" },
        { "POST", $"/api/v1/activity/admin/strikes/{Guid.Empty}/unrevoke" },
        { "POST", "/api/v1/activity/admin/excuses" },
        { "PUT", $"/api/v1/activity/admin/excuses/{Guid.Empty}" },
        { "DELETE", $"/api/v1/activity/admin/excuses/{Guid.Empty}" },
        { "POST", "/api/v1/activity/admin/link-requests/complete" },
        { "POST", "/api/v1/activity/admin/link-requests/cancel" },
        { "POST", "/api/v1/activity/admin/links/unlink" },
    };

    [Theory]
    [MemberData(nameof(AdminWriteRoutes))]
    public async Task Admin_write_endpoints_reject_anonymous_and_non_admin_callers(string method, string route)
    {
        var anonymous = new HttpRequestMessage(new HttpMethod(method), route)
        {
            Content = JsonContent.Create(new { })
        };
        (await _client.SendAsync(anonymous)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var asMember = Authorized(new HttpMethod(method), route, MemberToken);
        asMember.Content = JsonContent.Create(new { });
        (await _client.SendAsync(asMember)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_endpoints_allow_a_guild_administrator()
    {
        var response = await _client.SendAsync(AuthorizedGet("/api/v1/activity/admin/last-check-time", AdminToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<LastCheckTimeDto>();
        // The main club isn't seeded in this test, so there is no check time yet — but the
        // endpoint itself must succeed for an admin.
        dto!.LastCheckTime.Should().BeNull();
    }

    [Fact]
    public async Task GET_me_reports_isAdmin_true_for_a_guild_administrator()
    {
        var response = await _client.SendAsync(AuthorizedGet("/api/v1/activity/me", AdminToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<MeDto>();
        dto!.IsAdmin.Should().BeTrue();
        dto.DiscordUserId.Should().Be(_adminDiscordId.ToString());
    }

    [Fact]
    public async Task GET_me_reports_isAdmin_false_for_a_regular_member()
    {
        var response = await _client.SendAsync(AuthorizedGet("/api/v1/activity/me", MemberToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<MeDto>();
        dto!.IsAdmin.Should().BeFalse();
    }

    [Fact]
    public async Task GET_strikes_lists_all_strikes_with_nicknames_and_expiry()
    {
        await SeedMemberAsync();
        var strikeId = await SeedStrikeAsync(revoked: false);

        var response = await _client.SendAsync(AuthorizedGet("/api/v1/activity/admin/strikes", AdminToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var strikes = await response.Content.ReadFromJsonAsync<List<AdminStrikeDto>>();
        var strike = strikes!.Single(s => s.StrikeId == strikeId);
        strike.Nickname.Should().Be(_memberNickname);
        strike.Revoked.Should().BeFalse();
        strike.ExpiresAt.Should().BeAfter(strike.Timestamp);
    }

    [Fact]
    public async Task GET_member_strikes_returns_the_status_for_a_nickname()
    {
        await SeedMemberAsync();
        await SeedStrikeAsync(revoked: false);
        await SeedStrikeAsync(revoked: true);

        var response = await _client.SendAsync(
            AuthorizedGet($"/api/v1/activity/admin/members/{_memberNickname}/strikes", AdminToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<AdminMemberStrikesDto>();
        dto!.Strikes.Should().HaveCount(2);
        dto.NumActiveStrikes.Should().Be(1);
    }

    [Fact]
    public async Task GET_excuses_lists_excuses_with_nicknames()
    {
        await SeedMemberAsync();
        await SeedExcuseAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(6));

        var response = await _client.SendAsync(
            AuthorizedGet($"/api/v1/activity/admin/excuses?nickname={_memberNickname}", AdminToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var excuses = await response.Content.ReadFromJsonAsync<List<AdminExcuseDto>>();
        excuses.Should().ContainSingle(e => e.Nickname == _memberNickname);
    }

    [Fact]
    public async Task GET_member_activity_returns_the_window_for_a_nickname()
    {
        await SeedMemberAsync();

        var response = await _client.SendAsync(
            AuthorizedGet($"/api/v1/activity/admin/members/{_memberNickname}/activity?daysBack=7", AdminToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<WeekActivityDto>();
        dto!.TotalXp.Should().Be(0);
    }

    [Fact]
    public async Task Strike_write_lifecycle_works()
    {
        await SeedMemberAsync();

        // Add
        var addResponse = await SendJsonAsync(HttpMethod.Post, "/api/v1/activity/admin/strikes",
            new AddStrikeRequest(_memberNickname, DateTimeOffset.UtcNow));
        addResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await addResponse.Content.ReadFromJsonAsync<StrikeCreatedDto>();

        // Revoke
        var revokeResponse = await SendJsonAsync(HttpMethod.Post,
            $"/api/v1/activity/admin/strikes/{created!.StrikeId}/revoke", new { });
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var revoked = await revokeResponse.Content.ReadFromJsonAsync<AdminStrikeDto>();
        revoked!.Revoked.Should().BeTrue();
        revoked.Nickname.Should().Be(_memberNickname);

        // Unrevoke
        var unrevokeResponse = await SendJsonAsync(HttpMethod.Post,
            $"/api/v1/activity/admin/strikes/{created.StrikeId}/unrevoke", new { });
        unrevokeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var unrevoked = await unrevokeResponse.Content.ReadFromJsonAsync<AdminStrikeDto>();
        unrevoked!.Revoked.Should().BeFalse();
    }

    [Fact]
    public async Task Excuse_write_lifecycle_works()
    {
        await SeedMemberAsync();
        var from = DateTimeOffset.UtcNow.AddDays(1);
        var to = from.AddDays(7);

        // Add
        var addResponse = await SendJsonAsync(HttpMethod.Post, "/api/v1/activity/admin/excuses",
            new AddExcuseRequest(_memberNickname, from, to));
        addResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await addResponse.Content.ReadFromJsonAsync<ExcuseCreatedDto>();

        // Update
        var updateResponse = await SendJsonAsync(HttpMethod.Put,
            $"/api/v1/activity/admin/excuses/{created!.ExcuseId}",
            new UpdateExcuseRequest(from, to.AddDays(3)));
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Remove, then removing again 404s
        var deleteResponse = await _client.SendAsync(
            Authorized(HttpMethod.Delete, $"/api/v1/activity/admin/excuses/{created.ExcuseId}", AdminToken));
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        deleteResponse = await _client.SendAsync(
            Authorized(HttpMethod.Delete, $"/api/v1/activity/admin/excuses/{created.ExcuseId}", AdminToken));
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Link_request_completion_requires_the_right_otp_and_links_the_account()
    {
        // OTPs are exactly 18 characters (see CompleteAccountLinkingCommandValidator).
        const string otp = "correct-otp-123456";
        await SeedUnlinkedUserAsync();
        await SeedLinkRequestAsync(otp);

        // Wrong (but well-formed) OTP → 400, request stays open.
        var wrongResponse = await SendJsonAsync(HttpMethod.Post, "/api/v1/activity/admin/link-requests/complete",
            new CompleteLinkRequestRequest(_memberDiscordId.ToString(), _memberUserId, "wrong-otp-12345678"));
        wrongResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Right OTP → linked.
        var completeResponse = await SendJsonAsync(HttpMethod.Post, "/api/v1/activity/admin/link-requests/complete",
            new CompleteLinkRequestRequest(_memberDiscordId.ToString(), _memberUserId, otp));
        completeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var completed = await completeResponse.Content.ReadFromJsonAsync<LinkCompletedDto>();
        completed!.Nickname.Should().Be(_memberNickname);

        // The open request is gone.
        var listResponse = await _client.SendAsync(AuthorizedGet("/api/v1/activity/admin/link-requests", AdminToken));
        var remaining = await listResponse.Content.ReadFromJsonAsync<List<AdminLinkRequestDto>>();
        remaining.Should().NotContain(r => r.DiscordUserId == _memberDiscordId.ToString());

        // And the account can be unlinked again.
        var unlinkResponse = await SendJsonAsync(HttpMethod.Post, "/api/v1/activity/admin/links/unlink",
            new UnlinkAccountsRequest(_memberDiscordId.ToString(), _memberUserId));
        unlinkResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Link_request_can_be_cancelled_by_an_admin()
    {
        await SeedLinkRequestAsync("some-otp-value");

        var response = await SendJsonAsync(HttpMethod.Post, "/api/v1/activity/admin/link-requests/cancel",
            new CancelLinkRequestRequest(_memberDiscordId.ToString(), _memberUserId));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Link_endpoints_reject_a_malformed_discord_user_id()
    {
        var response = await SendJsonAsync(HttpMethod.Post, "/api/v1/activity/admin/links/unlink",
            new UnlinkAccountsRequest("not-a-snowflake", _memberUserId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GET_link_requests_lists_open_requests_but_never_the_otp()
    {
        // The OTP column is varchar(18); keep the sentinel within that.
        const string secretOtp = "otp-secret-1234567";
        await SeedLinkRequestAsync(secretOtp);

        var response = await _client.SendAsync(AuthorizedGet("/api/v1/activity/admin/link-requests", AdminToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync();
        // The OTP proves account ownership via GeoGuessr DM; it must be structurally absent from
        // the admin payload, not just renamed.
        raw.Should().NotContain(secretOtp);
        raw.Should().NotContainEquivalentOf("oneTimePassword");

        var requests = await response.Content.ReadFromJsonAsync<List<AdminLinkRequestDto>>();
        requests.Should().Contain(r =>
            r.DiscordUserId == _memberDiscordId.ToString() && r.GeoGuessrUserId == _memberUserId);
    }

    private static HttpRequestMessage AuthorizedGet(string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<HttpResponseMessage> SendJsonAsync<T>(HttpMethod method, string path, T body)
    {
        var request = Authorized(method, path, AdminToken);
        request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private async Task SeedUnlinkedUserAsync()
    {
        await using var db = _fixture.CreateDbContext();
        db.Add(GeoGuessrUser.Create(_memberUserId, _memberNickname, discordUserId: null));
        await db.SaveChangesAsync();
    }

    private async Task SeedMemberAsync()
    {
        await using var db = _fixture.CreateDbContext();
        db.Add(Club.Create(_mainClubId, "Main Club", level: 3));
        var user = GeoGuessrUser.Create(_memberUserId, _memberNickname, _memberDiscordId);
        db.Add(ClubMember.Create(user, _mainClubId, xp: 0, joinedAt: DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedStrikeAsync(bool revoked)
    {
        await using var db = _fixture.CreateDbContext();
        var strike = ClubMemberStrike.Create(_memberUserId, DateTimeOffset.UtcNow.AddDays(-1));
        if (revoked)
        {
            strike.Revoke();
        }
        db.Add(strike);
        await db.SaveChangesAsync();
        return strike.StrikeId;
    }

    private async Task<Guid> SeedExcuseAsync(DateTimeOffset from, DateTimeOffset to)
    {
        await using var db = _fixture.CreateDbContext();
        var excuse = ClubMemberExcuse.Create(_memberUserId, from, to);
        db.Add(excuse);
        await db.SaveChangesAsync();
        return excuse.ExcuseId;
    }

    private async Task SeedLinkRequestAsync(string oneTimePassword)
    {
        await using var db = _fixture.CreateDbContext();
        db.Add(GeoGuessrAccountLinkingRequest.Create(_memberDiscordId, _memberUserId, oneTimePassword));
        await db.SaveChangesAsync();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _baseFactory.DisposeAsync();
    }

    /// <summary>Maps known bearer tokens to Discord user ids; anything else is rejected.</summary>
    private sealed class StubOAuthService(IReadOnlyDictionary<string, ulong> tokenToUserId) : IDiscordOAuthService
    {
        public Task<Result<string>> ExchangeCodeForTokenAsync(string code, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<string>.Failure(Error.Unauthorized("activity.bad_code", "not used here")));

        public Task<Result<ulong>> GetUserIdAsync(string accessToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(tokenToUserId.TryGetValue(accessToken, out var userId)
                ? Result<ulong>.Success(userId)
                : Result<ulong>.Failure(Error.Unauthorized("activity.bad_token", "bad token")));
    }

    /// <summary>Only the configured Discord user counts as a guild administrator.</summary>
    private sealed class StubPermissionAccess(ulong adminDiscordId) : IDiscordMemberPermissionAccess
    {
        public Task<bool> IsAdministratorAsync(ulong discordUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(discordUserId == adminDiscordId);
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

    private sealed class StubServerRolesAccess : IDiscordServerRolesAccess
    {
        public Task<int> RemoveRoleFromAllPlayersAsync(ulong roleId, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task RemoveRolesFromUserAsync(ulong userId, IEnumerable<ulong> roleIds,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RemoveRoleFromPlayersAsync(IEnumerable<ulong> userIds, ulong roleId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task AddRoleToMembersByUserIdsAsync(IEnumerable<ulong> userIds, ulong roleId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<List<ulong>> ReadMembersWithRoleAsync(ulong roleId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<ulong>());
    }

    private sealed class StubTextChannelAccess : IDiscordTextChannelAccess
    {
        public Task<ulong?> CreatePrivateTextChannelAsync(ulong categoryId, string name, string description,
            IEnumerable<ulong>? allowedDiscordUserIds, IEnumerable<ulong>? allowedRoleIds,
            CancellationToken cancellationToken = default) => Task.FromResult<ulong?>(null);

        public Task UpdateTextChannelAsync(TextChannel newTextChannel, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> DeleteTextChannelAsync(ulong textChannelId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<ulong?> ReadLastMessageOfUserAsync(ulong userId, ulong channelId, int numMessageSearchlimit,
            CancellationToken cancellationToken = default) => Task.FromResult<ulong?>(null);
    }
}
