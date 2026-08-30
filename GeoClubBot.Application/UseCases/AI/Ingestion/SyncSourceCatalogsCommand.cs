using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.Abstractions;
using UseCases.OutputPorts.AI.Ingestion;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.AI.Ingestion;

/// <summary>
/// Refreshes the list of known sources from each family that publishes its own index.
///
/// Discovery is separated from ingestion so a catalogue refresh is cheap and can run often, while the
/// expensive embedding work is paced independently against the daily allowance.
/// </summary>
public sealed record SyncSourceCatalogsCommand(string? SourceType = null) : ICommand<Result<CatalogSyncReport>>;

public sealed record CatalogSyncReport(int Discovered, int Added, int Updated, int Tombstoned);

public sealed partial class SyncSourceCatalogsHandler(
    IEnumerable<ISourceCatalog> catalogs,
    IKnowledgeSourceRepository sources,
    ILogger<SyncSourceCatalogsHandler> logger)
    : IRequestHandler<SyncSourceCatalogsCommand, Result<CatalogSyncReport>>
{
    public async Task<Result<CatalogSyncReport>> Handle(
        SyncSourceCatalogsCommand request,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var added = 0;
        var updated = 0;
        var tombstoned = 0;

        var selected = request.SourceType is { } sourceType
            ? catalogs.Where(catalog => catalog.SourceType.Equals(sourceType, StringComparison.OrdinalIgnoreCase))
            : catalogs;

        // Every known source is loaded once and matched on the full (type, key) pair. A catalogue
        // does not necessarily publish a single type — a library of links yields guides, documents,
        // albums and more — so its own SourceType says nothing about what it will produce.
        var existing = await sources.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        var byKey = existing.ToDictionary(
            source => BuildKey(source.SourceType, source.NaturalKey), StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var syncedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Whether this run saw everything. Only then can an unlisted source be called stale with any
        // confidence; otherwise the absence may just be the part we could not read.
        var completePicture = request.SourceType is null;

        foreach (var catalog in selected)
        {
            var listed = await catalog.ListAsync(cancellationToken).ConfigureAwait(false);
            if (listed.IsFailure)
            {
                LogCatalogFailed(logger, catalog.SourceType, listed.Error.Message);
                completePicture = false;
                continue;
            }

            foreach (var descriptor in listed.Value)
            {
                var key = BuildKey(descriptor.SourceType, descriptor.NaturalKey);
                syncedTypes.Add(descriptor.SourceType);

                // The same resource is routinely listed twice — once by the site that publishes it
                // and again by a library that links to it. The first listing wins.
                if (!seen.Add(key))
                {
                    continue;
                }

                if (byKey.TryGetValue(key, out var source))
                {
                    // Metadata only: ingestion state and failure counts belong to the ingest run.
                    source.UpdateMetadata(
                        descriptor.Url.ToString(), descriptor.Title, descriptor.Country,
                        descriptor.Continent, descriptor.Author, descriptor.Priority);
                    updated++;
                    continue;
                }

                var created = KnowledgeSource.Create(
                    descriptor.SourceType, descriptor.NaturalKey, descriptor.Url.ToString(),
                    KnowledgeSourceOrigin.Sync, now, descriptor.Title, descriptor.Country,
                    descriptor.Continent, descriptor.Author, descriptor.Priority);

                if (descriptor.UnsupportedReason is { } reason)
                {
                    // Recorded, not queued: it never enters the ingest queue, but an admin can still
                    // see it and read why it is not indexed.
                    created.MarkSkipped(reason, now);
                }

                sources.Add(created);

                // Registered immediately so a later catalogue listing the same resource updates this
                // one instead of inserting a second row, which the unique index would reject.
                byKey[key] = created;
                added++;
            }
        }

        // When every catalogue was read, anything unlisted really has gone. When some could not be
        // read, the sweep is narrowed to types that were actually listed — a catalogue that failed
        // must not look like a catalogue that dropped everything it used to publish. The asymmetry is
        // deliberate: a wrong tombstone silently removes content from answers, while a missed one
        // only leaves something stale that /ai sources still shows.
        foreach (var source in existing.Where(source =>
                     source.Origin == KnowledgeSourceOrigin.Sync
                     && source.RemovedFromSyncAtUtc is null
                     && (completePicture || syncedTypes.Contains(source.SourceType))
                     && !seen.Contains(BuildKey(source.SourceType, source.NaturalKey))))
        {
            // Tombstoned rather than deleted: an upstream edit that temporarily drops a page
            // should not silently discard everything indexed from it.
            source.MarkRemovedFromSync(now);
            tombstoned++;
        }

        LogSynced(logger, seen.Count, added, updated, tombstoned);
        return new CatalogSyncReport(seen.Count, added, updated, tombstoned);
    }

    /// <summary>A source is identified by its family and its key within that family, never by either alone.</summary>
    private static string BuildKey(string sourceType, string naturalKey) => $"{sourceType}|{naturalKey}";

    [LoggerMessage(LogLevel.Information,
        "Source catalog sync: {Discovered} listed, {Added} new, {Updated} refreshed, {Tombstoned} no longer listed.")]
    static partial void LogSynced(ILogger logger, int discovered, int added, int updated, int tombstoned);

    [LoggerMessage(LogLevel.Warning, "Could not list sources for {SourceType}: {Reason}")]
    static partial void LogCatalogFailed(ILogger logger, string sourceType, string reason);
}
