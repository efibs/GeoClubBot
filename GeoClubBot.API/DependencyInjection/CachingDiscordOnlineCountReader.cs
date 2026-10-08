using Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Observability;
using UseCases.OutputPorts.Discord;

namespace GeoClubBot.DependencyInjection;

/// <summary>
/// Reads the server's online member count from Discord, cached briefly because every visit of the
/// club website asks for it. A failed read is not cached, so the next call tries again.
/// </summary>
public partial class CachingDiscordOnlineCountReader(
    IDiscordServerStatsAccess serverStats,
    IMemoryCache cache,
    IOptions<DiscordConfiguration> config,
    TimeProvider timeProvider,
    ILogger<CachingDiscordOnlineCountReader> logger) : IDiscordOnlineCountReader
{
    private const string CacheName = "discord_online_count";
    private const string CacheKey = "DiscordOnlineCount";

    public async Task<DiscordOnlineCount?> ReadOnlineCountAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue<DiscordOnlineCount>(CacheKey, out var cached) && cached is not null)
        {
            CacheMetrics.RecordHit(CacheName);
            return cached;
        }

        CacheMetrics.RecordMiss(CacheName);

        try
        {
            var online = await serverStats.ReadApproximateOnlineCountAsync(cancellationToken).ConfigureAwait(false);
            var count = new DiscordOnlineCount(online, timeProvider.GetUtcNow());

            cache.Set(CacheKey, count, config.Value.OnlineCountCacheTimeToLive);
            return count;
        }
        // A request timeout is an OperationCanceledException too; only the caller's own cancellation
        // is passed on.
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogOnlineCountReadFailed(ex);
            return null;
        }
    }

    [LoggerMessage(LogLevel.Warning, "Reading the Discord server's online count failed.")]
    partial void LogOnlineCountReadFailed(Exception ex);
}
