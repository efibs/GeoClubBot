using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Infrastructure.OutputAdapters.GeoGuessr;
using UseCases.OutputPorts.GeoGuessr;
using Xunit;

namespace GeoClubBot.Tests.GeoGuessr;

/// <summary>
/// Drives <see cref="RefitGeoGuessrClient"/> over a stub handler, so Refit's generated client and
/// <see cref="GeoGuessrJsonContext"/> are what build each request and read each response (#533). The
/// URLs pinned here are the ones the reflection-based client sent before the switch: GeoGuessr's
/// endpoints are only known to accept these.
/// </summary>
public sealed class RefitGeoGuessrClientTests
{
    private static readonly Guid ClubId = Guid.Parse("4d1b3f5c-6a7e-4f80-9a1b-2c3d4e5f6a7b");

    [Fact]
    public async Task ReadClubActivities_FlattensTheQueryObjectAndReadsTheItems()
    {
        var handler = new StubHandler("""
            {"items":[{"userId":"u1","type":5,"xpReward":20,"recordedAt":"2026-11-20T10:15:00.000Z","challengeToken":null}],
             "paginationToken":"next","unknownField":1}
            """);

        var response = await Client(handler).ReadClubActivitiesAsync(
            ClubId, new ReadClubActivitiesQueryParams { Limit = 50, PaginationToken = "abc" }, CancellationToken.None);

        handler.Requests.Should().ContainSingle().Which.Should().Be(
            $"GET https://geoguessr.test/api/v4/clubs/{ClubId}/activities?Limit=50&PaginationToken=abc");
        response.PaginationToken.Should().Be("next");
        var item = response.Items.Should().ContainSingle().Subject;
        item.UserId.Should().Be("u1");
        item.Type.Should().Be(5);
        item.XpReward.Should().Be(20);
        item.RecordedAt.Should().Be(new DateTimeOffset(2026, 11, 20, 10, 15, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task ReadClubActivities_LeavesANullPaginationTokenOut()
    {
        var handler = new StubHandler("""{"items":[]}""");

        await Client(handler).ReadClubActivitiesAsync(
            ClubId, new ReadClubActivitiesQueryParams(), CancellationToken.None);

        handler.Requests.Should().ContainSingle().Which.Should().EndWith("/activities?Limit=25");
    }

    [Fact]
    public async Task ReadClubApplications_SendsThePaginationTokenOnlyWhenThereIsOne()
    {
        var handler = new StubHandler($$"""{"items":[{{User("u2", "Applicant", "userId")}}]}""");
        var client = Client(handler);

        var first = await client.ReadClubApplicationsAsync(ClubId, cancellationToken: CancellationToken.None);
        await client.ReadClubApplicationsAsync(ClubId, "tok", CancellationToken.None);

        handler.Requests.Should().Equal(
            $"GET https://geoguessr.test/api/v4/clubs/{ClubId}/applications",
            $"GET https://geoguessr.test/api/v4/clubs/{ClubId}/applications?paginationToken=tok");
        first.Items.Should().ContainSingle().Which.UserId.Should().Be("u2");
    }

    [Fact]
    public async Task ReadHighscores_FlattensTheQueryObjectAndReadsTheRoundFields()
    {
        var handler = new StubHandler(await Fixture("geoguessr-challenge-highscores-20261113.json"));

        var response = await Client(handler).ReadHighscoresAsync(
            "challenge-token", new ReadHighscoresQueryParams { Limit = 26, MinRounds = 5 }, CancellationToken.None);

        handler.Requests.Should().ContainSingle().Which.Should().Be(
            "GET https://geoguessr.test/api/v3/results/highscores/challenge-token?Friends=False&Limit=26&MinRounds=5");
        var game = response.Items[0].Game;
        game.Rounds.Should().HaveCount(5).And.OnlyContain(round =>
            round.Lat != null && round.Lng != null && round.PanoId != null && round.StreakLocationCode != null);
        game.Player.Guesses.Should().HaveCount(5).And.OnlyContain(guess =>
            guess.Lat != null && guess.DistanceInMeters != null && guess.RoundScoreInPoints != null);
    }

    [Fact]
    public async Task ReadGame_ReadsTheGameAndAnswersNullForAMissingOne()
    {
        var found = new StubHandler(await Fixture("geoguessr-game-20261121.json"));
        var missing = new StubHandler("""{"message":"Game not found"}""", HttpStatusCode.NotFound);

        var game = await Client(found).ReadGameAsync("game-token", CancellationToken.None);
        var none = await Client(missing).ReadGameAsync("gone", CancellationToken.None);

        found.Requests.Should().ContainSingle().Which.Should().Be("GET https://geoguessr.test/api/v3/games/game-token");
        game.Should().NotBeNull();
        game!.Rounds.Should().NotBeEmpty().And.OnlyContain(round => round.Lat != null && round.Lng != null);
        game.Player.Guesses.Should().NotBeEmpty();
        none.Should().BeNull();
    }

    [Fact]
    public async Task CreateChallenge_WritesTheBodyInCamelCase()
    {
        var handler = new StubHandler("""{"token":"new-challenge"}""");

        var response = await Client(handler).CreateChallengeAsync(new PostChallengeRequestDto
        {
            AccessLevel = 1,
            ChallengeType = 0,
            ForbidMoving = true,
            ForbidRotating = false,
            ForbidZooming = true,
            Map = "world",
            TimeLimit = 60
        }, CancellationToken.None);

        handler.Requests.Should().ContainSingle().Which.Should().Be("POST https://geoguessr.test/api/v3/challenges");
        using var body = JsonDocument.Parse(handler.Bodies.Single()!);
        body.RootElement.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            "accessLevel", "challengeType", "forbidMoving", "forbidRotating", "forbidZooming", "map", "timeLimit");
        body.RootElement.GetProperty("map").GetString().Should().Be("world");
        body.RootElement.GetProperty("timeLimit").GetInt32().Should().Be(60);
        response.Token.Should().Be("new-challenge");
    }

    [Fact]
    public async Task ReadPreviousClubMissionBoard_AnswersNullForANullBody()
    {
        var handler = new StubHandler("null");

        var board = await Client(handler).ReadPreviousClubMissionBoardAsync(CancellationToken.None);

        handler.Requests.Should().ContainSingle().Which.Should().Be("GET https://geoguessr.test/api/v4/missions/club/board/previous");
        board.Should().BeNull();
    }

    [Fact]
    public async Task ReadClubMembers_ReadsAListReadingNamesCaseInsensitively()
    {
        var handler = new StubHandler($$"""
            [{"User":{{User("u3", "Member", "UserID")}},"role":1,"joinedAt":"2026-01-02T03:04:05Z","xp":120,"weeklyXp":20}]
            """);

        var members = await Client(handler).ReadClubMembersAsync(ClubId, CancellationToken.None);

        handler.Requests.Should().ContainSingle().Which.Should().Be($"GET https://geoguessr.test/api/v4/clubs/{ClubId}/members");
        var member = members.Should().ContainSingle().Subject;
        member.User.UserId.Should().Be("u3");
        member.User.Nick.Should().Be("Member");
        member.Xp.Should().Be(120);
        member.JoinedAt.Should().Be(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
    }

    /// <summary>A user as GeoGuessr lists one; <paramref name="idKey"/> spells the id's name.</summary>
    private static string User(string id, string nick, string idKey) =>
        $$"""
        {"{{idKey}}":"{{id}}","nick":"{{nick}}","avatar":"a","fullBodyAvatar":"b","borderUrl":"c","isVerified":false,
         "flair":0,"countryCode":"de","tierId":1,"clubUserType":0}
        """;

    private static RefitGeoGuessrClient Client(HttpMessageHandler handler) =>
        RefitGeoGuessrClient.FromHttpClient(new HttpClient(handler) { BaseAddress = new Uri("https://geoguessr.test/api") });

    private static Task<string> Fixture(string name) =>
        File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "AI", name));

    private sealed class StubHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        public List<string?> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method} {request.RequestUri}");
            Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(status)
            {
                RequestMessage = request,
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }
}
