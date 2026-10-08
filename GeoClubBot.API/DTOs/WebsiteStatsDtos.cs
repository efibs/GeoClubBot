using System.Text.Json.Serialization;

namespace GeoClubBot.DTOs;

// The body of GET /api/v1/stats is a contract with the website's JSON schema: every key is always
// present, no other key is allowed. The names are pinned so no naming policy can change them (the
// default camelCase would write "geoGuessr"), and the null sections are always written.

public record WebsiteStatsDto(
    [property: JsonPropertyName("geoguessr"), JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    WebsiteGeoGuessrStatsDto? GeoGuessr,
    [property: JsonPropertyName("discord"), JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    WebsiteDiscordStatsDto? Discord);

/// <param name="UpdatedAt">
/// UTC. A <see cref="DateTime"/>, because System.Text.Json writes a <see cref="DateTimeOffset"/> as
/// "+00:00" and the schema wants a trailing "Z".
/// </param>
public record WebsiteGeoGuessrStatsDto(
    [property: JsonPropertyName("updatedAt")] DateTime UpdatedAt,
    [property: JsonPropertyName("totalClubs")] int TotalClubs,
    [property: JsonPropertyName("clubs")] WebsiteClubsDto Clubs);

public record WebsiteClubsDto(
    [property: JsonPropertyName("main")] WebsiteClubStatsDto Main,
    [property: JsonPropertyName("second")] WebsiteClubStatsDto Second);

public record WebsiteClubStatsDto(
    [property: JsonPropertyName("rank")] int Rank,
    [property: JsonPropertyName("level")] int Level,
    [property: JsonPropertyName("members")] int Members,
    [property: JsonPropertyName("xp")] int Xp);

/// <param name="UpdatedAt">UTC, for the same reason as <see cref="WebsiteGeoGuessrStatsDto.UpdatedAt"/>.</param>
public record WebsiteDiscordStatsDto(
    [property: JsonPropertyName("updatedAt")] DateTime UpdatedAt,
    [property: JsonPropertyName("online")] int Online);
