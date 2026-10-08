using Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Observability;
using UseCases.OutputPorts.GeoGuessr;

namespace GeoClubBot.DependencyInjection;

/// <summary>
/// Reads a club with that club's own client, so its token, rate limiter, retries and circuit
/// breaker all apply.
///
/// Unlike the mission board, a failed read is not cached: the website asks for these figures, and
/// a cached failure would hide them for the whole time to live after a single hiccup. The circuit
/// breaker is what keeps an outage from being hammered.
/// </summary>
public partial class CachingGeoGuessrClubReader(
    IGeoGuessrClientFactory clientFactory,
    IMemoryCache cache,
    IOptions<GeoGuessrConfiguration> config,
    TimeProvider timeProvider,
    ILogger<CachingGeoGuessrClubReader> logger) : IGeoGuessrClubReader
{
    private const string CacheName = "geoguessr_clubs";

    public async Task<GeoGuessrClubSnapshot?> ReadClubAsync(Guid clubId, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"GeoGuessrClub:{clubId}";

        if (cache.TryGetValue<GeoGuessrClubSnapshot>(cacheKey, out var cached) && cached is not null)
        {
            CacheMetrics.RecordHit(CacheName);
            return cached;
        }

        CacheMetrics.RecordMiss(CacheName);

        try
        {
            var club = await clientFactory.CreateClient(clubId)
                .ReadClubAsync(clubId, cancellationToken)
                .ConfigureAwait(false);

            // The figures are copied out: the DTO is mutable (the mock even hands out its live copy).
            var snapshot = new GeoGuessrClubSnapshot(
                club.ClubId,
                club.Level,
                club.MemberCount,
                club.Stats.TotalXp,
                club.Stats.GlobalXpRank,
                club.Stats.TotalClubs,
                timeProvider.GetUtcNow());

            cache.Set(cacheKey, snapshot, config.Value.ClubCacheTimeToLive);
            return snapshot;
        }
        // An HttpClient timeout is an OperationCanceledException too; only the caller's own
        // cancellation is passed on.
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogClubReadFailed(ex, clubId);
            return null;
        }
    }

    [LoggerMessage(LogLevel.Warning, "Reading club {ClubId} from GeoGuessr failed.")]
    partial void LogClubReadFailed(Exception ex, Guid clubId);
}
