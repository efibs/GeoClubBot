using Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Observability;
using UseCases.OutputPorts.GeoGuessr;

namespace GeoClubBot.DependencyInjection;

public partial class CachingGeoGuessrUserProfileReader(
    IGeoGuessrClientFactory clientFactory,
    IMemoryCache cache,
    IOptions<GeoGuessrConfiguration> config,
    ILogger<CachingGeoGuessrUserProfileReader> logger) : IGeoGuessrUserProfileReader
{
    private const string CacheName = "geoguessr_user_profiles";

    public async Task<UserDto?> ReadUserProfileAsync(string userId, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"GeoGuessrUserProfile:{userId}";

        if (cache.TryGetValue<UserDto?>(cacheKey, out var cached))
        {
            CacheMetrics.RecordHit(CacheName);
            return cached;
        }

        CacheMetrics.RecordMiss(CacheName);
        LogCacheMiss(userId);
        var profile = await clientFactory.CreateUserProfileClient()
            .ReadUserAsync(userId, cancellationToken).ConfigureAwait(false);
        cache.Set(cacheKey, profile, config.Value.UserProfileCacheTimeToLive);
        return profile;
    }

    [LoggerMessage(LogLevel.Debug, "User profile cache miss for user {UserId}, fetching from GeoGuessr API.")]
    partial void LogCacheMiss(string userId);
}
