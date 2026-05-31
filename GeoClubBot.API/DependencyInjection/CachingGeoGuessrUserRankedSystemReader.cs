using Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using UseCases.Observability;
using UseCases.OutputPorts.GeoGuessr;

namespace GeoClubBot.DependencyInjection;

public partial class CachingGeoGuessrUserRankedSystemReader(
    IGeoGuessrClientFactory clientFactory,
    IMemoryCache cache,
    IOptions<GeoGuessrConfiguration> config,
    ILogger<CachingGeoGuessrUserRankedSystemReader> logger) : IGeoGuessrUserRankedSystemReader
{
    private const string CacheName = "geoguessr_user_ranked_system";

    public async Task<RankedProgressResponseDto?> ReadRankedProgressOfUserAsync(string userId,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"GeoGuessrUserRankedSystemProgress:{userId}";

        if (cache.TryGetValue<RankedProgressResponseDto?>(cacheKey, out var cached))
        {
            CacheMetrics.RecordHit(CacheName);
            return cached;
        }

        CacheMetrics.RecordMiss(CacheName);
        LogCacheMiss(userId);
        var rankedProgress = await clientFactory.CreateUserProfileClient()
            .ReadRankedProgressOfUserAsync(userId, cancellationToken).ConfigureAwait(false);
        cache.Set(cacheKey, rankedProgress, config.Value.UserProfileCacheTimeToLive);
        return rankedProgress;
    }

    public async Task<RankedPeakRatingResponseDto?> ReadRankedPeakRatingOfUserAsync(string userId,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"GeoGuessrUserRankedSystemPeakRating:{userId}";

        if (cache.TryGetValue<RankedPeakRatingResponseDto?>(cacheKey, out var cached))
        {
            CacheMetrics.RecordHit(CacheName);
            return cached;
        }

        CacheMetrics.RecordMiss(CacheName);
        LogCacheMiss(userId);
        var peakRating = await clientFactory.CreateUserProfileClient()
            .ReadRankedPeakRatingOfUserAsync(userId, cancellationToken).ConfigureAwait(false);
        cache.Set(cacheKey, peakRating, config.Value.UserProfileCacheTimeToLive);
        return peakRating;
    }

    [LoggerMessage(LogLevel.Debug, "User ranked system cache miss for user {UserId}, fetching from GeoGuessr API.")]
    partial void LogCacheMiss(string userId);
}
