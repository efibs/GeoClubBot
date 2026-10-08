using Extensions;
using UseCases.UseCases.Website;

namespace GeoClubBot.DTOs.Assemblers;

public static class WebsiteStatsDtoAssembler
{
    public static WebsiteStatsDto AssembleDto(WebsiteStats stats) => new(
        stats.GeoGuessr is { } geoGuessr
            ? new WebsiteGeoGuessrStatsDto(
                ToUtcSeconds(geoGuessr.UpdatedAt),
                geoGuessr.TotalClubs,
                new WebsiteClubsDto(AssembleDto(geoGuessr.Main), AssembleDto(geoGuessr.Second)))
            : null,
        stats.Discord is { } discord
            ? new WebsiteDiscordStatsDto(ToUtcSeconds(discord.UpdatedAt), discord.Online)
            : null);

    private static WebsiteClubStatsDto AssembleDto(WebsiteClubStats club) =>
        new(club.Rank, club.Level, club.Members, club.Xp);

    /// <summary>
    /// Whole seconds of UTC, so it is written as "2026-10-08T09:00:12Z". Converted to UTC first,
    /// because <see cref="DateTimeOffsetExtensions.Truncate"/> works on the clock time and stamps
    /// offset zero.
    /// </summary>
    private static DateTime ToUtcSeconds(DateTimeOffset time) =>
        time.ToUniversalTime().Truncate(TimeSpan.FromSeconds(1)).UtcDateTime;
}
