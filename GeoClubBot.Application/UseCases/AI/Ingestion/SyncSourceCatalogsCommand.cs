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
        var discovered = 0;
        var added = 0;
        var updated = 0;
        var tombstoned = 0;

        var selected = request.SourceType is { } sourceType
            ? catalogs.Where(catalog => catalog.SourceType.Equals(sourceType, StringComparison.OrdinalIgnoreCase))
            : catalogs;

        foreach (var catalog in selected)
        {
            var listed = await catalog.ListAsync(cancellationToken).ConfigureAwait(false);
            if (listed.IsFailure)
            {
                LogCatalogFailed(logger, catalog.SourceType, listed.Error.Message);
                continue;
            }

            var existing = await sources.ReadByTypeAsync(catalog.SourceType, cancellationToken).ConfigureAwait(false);
            var existingByKey = existing.ToDictionary(source => source.NaturalKey, StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var descriptor in listed.Value)
            {
                discovered++;
                seen.Add(descriptor.NaturalKey);

                if (existingByKey.TryGetValue(descriptor.NaturalKey, out var source))
                {
                    // Metadata only: ingestion state and failure counts belong to the ingest run.
                    source.UpdateMetadata(
                        descriptor.Url.ToString(), descriptor.Title, descriptor.Country,
                        descriptor.Continent, descriptor.Author, descriptor.Priority);
                    updated++;
                    continue;
                }

                sources.Add(KnowledgeSource.Create(
                    descriptor.SourceType, descriptor.NaturalKey, descriptor.Url.ToString(),
                    KnowledgeSourceOrigin.Sync, now, descriptor.Title, descriptor.Country,
                    descriptor.Continent, descriptor.Author, descriptor.Priority));
                added++;
            }

            foreach (var source in existing.Where(source =>
                         source.Origin == KnowledgeSourceOrigin.Sync
                         && source.RemovedFromSyncAtUtc is null
                         && !seen.Contains(source.NaturalKey)))
            {
                // Tombstoned rather than deleted: an upstream edit that temporarily drops a page
                // should not silently discard everything indexed from it.
                source.MarkRemovedFromSync(now);
                tombstoned++;
            }
        }

        LogSynced(logger, discovered, added, updated, tombstoned);
        return new CatalogSyncReport(discovered, added, updated, tombstoned);
    }

    [LoggerMessage(LogLevel.Information,
        "Source catalog sync: {Discovered} listed, {Added} new, {Updated} refreshed, {Tombstoned} no longer listed.")]
    static partial void LogSynced(ILogger logger, int discovered, int added, int updated, int tombstoned);

    [LoggerMessage(LogLevel.Warning, "Could not list sources for {SourceType}: {Reason}")]
    static partial void LogCatalogFailed(ILogger logger, string sourceType, string reason);
}
