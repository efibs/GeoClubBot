using Configuration;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;
using Utilities;

namespace UseCases.UseCases.Website;

/// <summary>The figures the club website shows: both clubs' GeoGuessr numbers and the online count.</summary>
public sealed record GetWebsiteStatsQuery : IQuery<Result<WebsiteStats>>;

/// <summary>A section is null when its source could not be read.</summary>
public sealed record WebsiteStats(WebsiteGeoGuessrStats? GeoGuessr, WebsiteDiscordStats? Discord);

/// <param name="UpdatedAt">When the older of the two clubs was read from GeoGuessr.</param>
public sealed record WebsiteGeoGuessrStats(
    DateTimeOffset UpdatedAt,
    int TotalClubs,
    WebsiteClubStats Main,
    WebsiteClubStats Second);

public sealed record WebsiteClubStats(int Rank, int Level, int Members, int Xp);

public sealed record WebsiteDiscordStats(DateTimeOffset UpdatedAt, int Online);

public sealed partial class GetWebsiteStatsHandler(
    IGeoGuessrClubReader clubReader,
    IDiscordOnlineCountReader onlineCountReader,
    IOptions<WebsiteConfiguration> websiteConfig,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    ILogger<GetWebsiteStatsHandler> logger)
    : IRequestHandler<GetWebsiteStatsQuery, Result<WebsiteStats>>
{
    public async Task<Result<WebsiteStats>> Handle(GetWebsiteStatsQuery request, CancellationToken cancellationToken)
    {
        if (!websiteConfig.Value.Enabled || websiteConfig.Value.SecondClubId is not { } secondClubId)
        {
            return Error.NotFound("website.disabled", "The website statistics are not enabled.");
        }

        // The sources fail independently, so each one only empties its own section.
        var mainTask = clubReader.ReadClubAsync(geoGuessrConfig.Value.MainClub.ClubId, cancellationToken);
        var secondTask = clubReader.ReadClubAsync(secondClubId, cancellationToken);
        var onlineTask = onlineCountReader.ReadOnlineCountAsync(cancellationToken);
        await Task.WhenAll(mainTask, secondTask, onlineTask).ConfigureAwait(false);

        var online = await onlineTask.ConfigureAwait(false);

        return new WebsiteStats(
            BuildGeoGuessrStats(await mainTask.ConfigureAwait(false), await secondTask.ConfigureAwait(false)),
            online is null ? null : new WebsiteDiscordStats(online.ReadAt, online.Online));
    }

    private WebsiteGeoGuessrStats? BuildGeoGuessrStats(GeoGuessrClubSnapshot? main, GeoGuessrClubSnapshot? second)
    {
        // The website shows both clubs together, so one missing club leaves out the whole section.
        if (main is null || second is null)
        {
            return null;
        }

        // The website's schema requires a rank and a club count of at least 1, and it rejects the
        // whole body otherwise — the online count included. Leave out just this section instead.
        if (main.GlobalXpRank < 1 || second.GlobalXpRank < 1 || main.TotalClubs < 1)
        {
            LogImplausibleClubFigures(main.GlobalXpRank, second.GlobalXpRank, main.TotalClubs);
            return null;
        }

        return new WebsiteGeoGuessrStats(
            main.ReadAt <= second.ReadAt ? main.ReadAt : second.ReadAt,
            main.TotalClubs,
            ToClubStats(main),
            ToClubStats(second));
    }

    private static WebsiteClubStats ToClubStats(GeoGuessrClubSnapshot club) =>
        new(club.GlobalXpRank, club.Level, club.MemberCount, club.TotalXp);

    [LoggerMessage(LogLevel.Warning,
        "GeoGuessr reported implausible club figures (main rank {MainRank}, second rank {SecondRank}, " +
        "total clubs {TotalClubs}). The website's GeoGuessr section is left out.")]
    partial void LogImplausibleClubFigures(int mainRank, int secondRank, int totalClubs);
}
