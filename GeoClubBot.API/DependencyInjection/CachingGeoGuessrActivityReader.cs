using Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.GeoGuessr;

namespace GeoClubBot.DependencyInjection;

public partial class CachingGeoGuessrActivityReader(
    IGeoGuessrClientFactory clientFactory,
    IMemoryCache cache,
    IOptions<GeoGuessrConfiguration> config,
    ILogger<CachingGeoGuessrActivityReader> logger) : IGeoGuessrActivityReader
{
    public async Task<IReadOnlyList<ReadClubActivitiesItemDto>> ReadTodaysActivitiesAsync(Guid clubId)
    {
        var today = DateTimeOffset.UtcNow.Date;
        var cacheKey = $"GeoGuessrActivities:{clubId}:{today:yyyy-MM-dd}";

        var cached = await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = config.Value.ActivityCacheTimeToLive;
            LogCacheMiss(clubId);
            return await _fetchTodaysActivitiesAsync(clubId, today).ConfigureAwait(false);
        }).ConfigureAwait(false);

        return cached ?? [];
    }

    private async Task<IReadOnlyList<ReadClubActivitiesItemDto>> _fetchTodaysActivitiesAsync(Guid clubId, DateTime today)
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

            var batch = await client.ReadClubActivitiesAsync(clubId, request).ConfigureAwait(false);

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

    public async Task<IReadOnlyList<ReadClubActivitiesItemDto>> ReadActivitiesSinceAsync(Guid clubId, DateTimeOffset since)
    {
        var cacheKey = $"GeoGuessrActivities:{clubId}:since:{since:yyyy-MM-ddTHH:mm:ssZ}";

        var cached = await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = config.Value.ActivityCacheTimeToLive;
            LogCacheMiss(clubId);
            return await _fetchActivitiesSinceAsync(clubId, since).ConfigureAwait(false);
        }).ConfigureAwait(false);

        return cached ?? [];
    }

    private async Task<IReadOnlyList<ReadClubActivitiesItemDto>> _fetchActivitiesSinceAsync(Guid clubId, DateTimeOffset since)
    {
        var client = clientFactory.CreateActivityClient();
        var activities = new List<ReadClubActivitiesItemDto>();
        string? paginationToken = null;

        while (true)
        {
            var batch = await client
                .ReadClubActivitiesAsync(clubId, new ReadClubActivitiesQueryParams { PaginationToken = paginationToken })
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
