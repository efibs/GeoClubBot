using System.Text.Json;
using FluentAssertions;
using GeoClubBot.DTOs.Assemblers;
using UseCases.UseCases.Website;
using Xunit;

namespace GeoClubBot.Tests.Api;

/// <summary>
/// The body of GET /api/v1/stats is checked by the website against its JSON schema, which rejects
/// the whole answer over a missing, extra or misnamed key. Serialized with MVC's defaults.
/// </summary>
public sealed class WebsiteStatsDtoAssemblerTests
{
    private static readonly JsonSerializerOptions MvcDefaults = new(JsonSerializerDefaults.Web);

    private static string Serialize(WebsiteStats stats) =>
        JsonSerializer.Serialize(WebsiteStatsDtoAssembler.AssembleDto(stats), MvcDefaults);

    [Fact]
    public void AssembleDto_WritesTheWebsitesSchema()
    {
        var stats = new WebsiteStats(
            new WebsiteGeoGuessrStats(
                new DateTimeOffset(2026, 10, 8, 9, 0, 12, TimeSpan.Zero),
                29168,
                new WebsiteClubStats(Rank: 3, Level: 36, Members: 30, Xp: 425770),
                new WebsiteClubStats(Rank: 221, Level: 20, Members: 25, Xp: 123400)),
            new WebsiteDiscordStats(new DateTimeOffset(2026, 10, 8, 9, 12, 40, TimeSpan.Zero), 18));

        Serialize(stats).Should().Be(
            """{"geoguessr":{"updatedAt":"2026-10-08T09:00:12Z","totalClubs":29168,"clubs":{"main":{"rank":3,"level":36,"members":30,"xp":425770},"second":{"rank":221,"level":20,"members":25,"xp":123400}}},"discord":{"updatedAt":"2026-10-08T09:12:40Z","online":18}}""");
    }

    [Fact]
    public void AssembleDto_WritesNull_ForASectionThatCouldNotBeRead()
    {
        Serialize(new WebsiteStats(null, null)).Should().Be("""{"geoguessr":null,"discord":null}""");
    }

    [Fact]
    public void AssembleDto_WritesTimesInUtc_InWholeSeconds()
    {
        // 11:12:40.987 at +02:00 is 09:12:40 UTC; the fraction is dropped, not rounded.
        var readAt = new DateTimeOffset(2026, 10, 8, 11, 12, 40, 987, TimeSpan.FromHours(2));

        var json = Serialize(new WebsiteStats(null, new WebsiteDiscordStats(readAt, 18)));

        json.Should().Be("""{"geoguessr":null,"discord":{"updatedAt":"2026-10-08T09:12:40Z","online":18}}""");
    }
}
