using System.Net;
using System.Net.Http.Json;
using Entities;
using FluentAssertions;
using GeoClubBot.DTOs;
using Xunit;

namespace GeoClubBot.Tests.Integration.E2E;

/// <summary>
/// End-to-end coverage of the club HTTP endpoint: a real request travels routing → controller →
/// repository (caching decorator + EF) → Postgres, and the <c>Result&lt;T&gt;</c> is mapped to an
/// HTTP status by the ResultExtensions middleware. Each test uses its own random club id so the
/// shared container can be reused without collisions.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ClubApiE2ETests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly Guid _clubId = Guid.NewGuid();
    private readonly GeoClubBotApiFactory _factory;
    private readonly HttpClient _client;

    public ClubApiE2ETests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _factory = new GeoClubBotApiFactory(fixture.ConnectionString, _clubId);
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task GET_club_returns_404_when_the_main_club_is_not_persisted()
    {
        var response = await _client.GetAsync("/api/v1/club");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GET_club_returns_the_persisted_main_club()
    {
        await SeedAsync(Club.Create(_clubId, "E2E Test Club", level: 7));

        var response = await _client.GetAsync("/api/v1/club");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<ClubDto>();
        dto.Should().NotBeNull();
        dto!.Name.Should().Be("E2E Test Club");
        dto.Level.Should().Be(7);
    }

    private async Task SeedAsync(Club club)
    {
        await using var db = _fixture.CreateDbContext();
        db.Add(club);
        await db.SaveChangesAsync();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }
}
