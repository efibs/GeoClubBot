using System.Diagnostics.Metrics;

namespace UseCases.Observability;

/// <summary>
/// Shared meter + counter for caching-decorator hit/miss telemetry. Each caching
/// adapter records into <see cref="CacheLookups"/> tagged with the cache name and
/// the outcome so dashboards can derive per-cache hit ratios.
/// </summary>
public static class CacheMetrics
{
    public static readonly Counter<long> CacheLookups = HandlerMetrics.Meter.CreateCounter<long>(
        name: "geoclubbot.cache.lookups",
        unit: "{lookup}",
        description: "Count of caching-decorator lookups, tagged with cache_name and outcome (hit|miss).");

    public static void RecordHit(string cacheName) =>
        CacheLookups.Add(1,
            new KeyValuePair<string, object?>("cache_name", cacheName),
            new KeyValuePair<string, object?>("outcome", "hit"));

    public static void RecordMiss(string cacheName) =>
        CacheLookups.Add(1,
            new KeyValuePair<string, object?>("cache_name", cacheName),
            new KeyValuePair<string, object?>("outcome", "miss"));
}
