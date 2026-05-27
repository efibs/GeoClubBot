using Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.GeoGuessr;

namespace GeoClubBot.DependencyInjection;

public partial class CachingGeoGuessrUserProfileReader(
    IGeoGuessrClientFactory clientFactory,
    IMemoryCache cache,
    IOptions<GeoGuessrConfiguration> config,
    ILogger<CachingGeoGuessrUserProfileReader> logger) : IGeoGuessrUserProfileReader
{
    public async Task<UserDto?> ReadUserProfileAsync(string userId, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"GeoGuessrUserProfile:{userId}";

        return await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = config.Value.UserProfileCacheTimeToLive;
            LogCacheMiss(userId);
            return await clientFactory.CreateUserProfileClient()
                .ReadUserAsync(userId, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    [LoggerMessage(LogLevel.Debug, "User profile cache miss for user {UserId}, fetching from GeoGuessr API.")]
    partial void LogCacheMiss(string userId);
}
