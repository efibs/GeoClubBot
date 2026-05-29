using Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Observability;
using UseCases.OutputPorts.GeoGuessr;

namespace GeoClubBot.DependencyInjection;

public partial class CachingGeoGuessrActivityReader(
    IGeoGuessrClientFactory clientFactory,
    IMemoryCache cache,
    IOptions<GeoGuessrConfiguration> config,
    ILogger<CachingGeoGuessrActivityReader> logger) : IGeoGuessrActivityReader
{
    private const string CacheName = "geoguessr_activities";

    public async Task<IReadOnlyList<ReadClubActivitiesItemDto>> ReadTodaysActivitiesAsync(Guid clubId, CancellationToken cancellationToken = default)
    {
        var today = DateTimeOffset.UtcNow.Date;
        var cacheKey = $"GeoGuessrActivities:{clubId}:{today:yyyy-MM-dd}";

        if (cache.TryGetValue<IReadOnlyList<ReadClubActivitiesItemDto>>(cacheKey, out var cached))
        {
            CacheMetrics.RecordHit(CacheName);
            return cached ?? [];
        }

        CacheMetrics.RecordMiss(CacheName);
        LogCacheMiss(clubId);
        var fetched = await FetchTodaysActivitiesAsync(clubId, today, cancellationToken).ConfigureAwait(false);
        cache.Set(cacheKey, fetched, config.Value.ActivityCacheTimeToLive);
        return fetched;
    }

    private async Task<IReadOnlyList<ReadClubActivitiesItemDto>> FetchTodaysActivitiesAsync(Guid clubId, DateTime today, CancellationToken cancellationToken)
    {
        var client = clientFactory.CreateActivityClient();
        var todaysActivities = new List<ReadClubActivitiesItemDto>();
        string? paginationToken = null;

        while (true)
        {
            var request = new ReadClubActivitiesQueryParams
            {
                PaginationToken = paginationToken
            };

            var batch = await client.ReadClubActivitiesAsync(clubId, request, cancellationToken).ConfigureAwait(false);

            if (batch.Items.Count == 0)
            {
                return todaysActivities;
            }

            var orderedActivities = batch.Items.OrderByDescending(i => i.RecordedAt);

            foreach (var activity in orderedActivities)
            {
                if (activity.RecordedAt.Date < today)
                {
                    return todaysActivities;
                }

                todaysActivities.Add(activity);
            }

            if (batch.PaginationToken is null)
            {
                return todaysActivities;
            }

            paginationToken = batch.PaginationToken;
        }
    }

    public async Task<IReadOnlyList<ReadClubActivitiesItemDto>> ReadActivitiesSinceAsync(Guid clubId, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"GeoGuessrActivities:{clubId}:since:{since:yyyy-MM-ddTHH:mm:ssZ}";

        if (cache.TryGetValue<IReadOnlyList<ReadClubActivitiesItemDto>>(cacheKey, out var cached))
        {
            CacheMetrics.RecordHit(CacheName);
            return cached ?? [];
        }

        CacheMetrics.RecordMiss(CacheName);
        LogCacheMiss(clubId);
        var fetched = await FetchActivitiesSinceAsync(clubId, since, cancellationToken).ConfigureAwait(false);
        cache.Set(cacheKey, fetched, config.Value.ActivityCacheTimeToLive);
        return fetched;
    }

    private async Task<IReadOnlyList<ReadClubActivitiesItemDto>> FetchActivitiesSinceAsync(Guid clubId, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var client = clientFactory.CreateActivityClient();
        var activities = new List<ReadClubActivitiesItemDto>();
        string? paginationToken = null;

        while (true)
        {
            var batch = await client
                .ReadClubActivitiesAsync(clubId, new ReadClubActivitiesQueryParams { PaginationToken = paginationToken }, cancellationToken)
                .ConfigureAwait(false);

            if (batch.Items.Count == 0)
                return activities;

            var reachedCutoff = false;
            foreach (var item in batch.Items.OrderByDescending(i => i.RecordedAt))
            {
                if (item.RecordedAt < since)
                {
                    reachedCutoff = true;
                    break;
                }

                activities.Add(item);
            }

            if (reachedCutoff || batch.PaginationToken is null)
                return activities;

            paginationToken = batch.PaginationToken;
        }
    }

    [LoggerMessage(LogLevel.Debug, "Activity cache miss for club {ClubId}, fetching from GeoGuessr API.")]
    partial void LogCacheMiss(Guid clubId);
}
