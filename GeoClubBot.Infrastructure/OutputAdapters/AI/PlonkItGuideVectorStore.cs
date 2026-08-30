using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using UseCases.OutputPorts.AI;

namespace Infrastructure.OutputAdapters.AI;

/// <summary>
/// Thin facade composing the page fetcher, embedder, and vector index. Owns the
/// lifecycle (init / rebuild) plus the per-rebuild semaphore that suppresses
/// concurrent searches mid-rebuild. Query call sites still use this facade today —
/// the search/lookup methods are pass-throughs.
/// </summary>
public sealed partial class PlonkItGuideVectorStore(
    IPlonkItPageFetcher fetcher,
    IPlonkItEmbedder embedder,
    IPlonkItVectorIndex index,
    ILogger<PlonkItGuideVectorStore> logger) : IPlonkItGuideVectorStore
{
    private readonly SemaphoreSlim _rebuildStoreLock = new(1, 1);

    public SemaphoreSlim RebuildStoreLock => _rebuildStoreLock;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _rebuildStoreLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await index.CollectionExistsAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            if (!await embedder.TestConnectionsAsync(cancellationToken).ConfigureAwait(false))
            {
                LogConnectionsUnavailable(logger);
                return;
            }

            await index.EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);

            await foreach (var statusUpdate in BuildStoreAsync(cancellationToken).ConfigureAwait(false))
            {
                LogStoreInitStatus(logger, statusUpdate);
            }

            LogStoreInitDone(logger);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to initialize Qdrant collection: {ex.Message}", ex);
        }
        finally
        {
            _rebuildStoreLock.Release();
        }
    }

    public async IAsyncEnumerable<string> RebuildStoreAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!await embedder.TestConnectionsAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return "Could not rebuild PlonkIt Guide vector store because not all connections could be established.";
            yield break;
        }

        await _rebuildStoreLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await index.CollectionExistsAsync(cancellationToken).ConfigureAwait(false))
            {
                await index.DeleteCollectionAsync(cancellationToken).ConfigureAwait(false);
            }

            await index.EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);

            await foreach (var statusUpdate in BuildStoreAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return statusUpdate;
            }
        }
        finally
        {
            _rebuildStoreLock.Release();
        }

        yield return "Rebuild done.";
    }

    public async Task<List<SectionRecord>> SearchSectionsAsync(string query, int limit = 5)
    {
        var queryVector = await embedder.EmbedQueryAsync(query).ConfigureAwait(false);
        return await index.SearchAsync(queryVector, limit).ConfigureAwait(false);
    }

    public Task<List<string>> GetUniqueCountriesAsync() => index.GetUniqueCountriesAsync();

    public Task<List<SectionRecord>> GetSectionsByCountryAsync(string country) =>
        index.GetSectionsByCountryAsync(country);

    private async IAsyncEnumerable<string> BuildStoreAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var evt in fetcher.EnumerateAsync(cancellationToken).ConfigureAwait(false))
        {
            switch (evt)
            {
                case PlonkItStatusEvent status:
                    yield return status.Message;
                    break;
                case PlonkItSectionEvent { Section: var section }:
                    string? error = null;
                    try
                    {
                        LogAddingSectionForCountry(logger, section.Country);
                        var vector = await embedder.EmbedSectionAsync(section, cancellationToken).ConfigureAwait(false);
                        await index.UpsertAsync(
                            Guid.NewGuid().ToString(), vector, section.InnerHtml, section.Source, section.Country,
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        error = $"Adding section for plonk-it-guide failed: {ex.Message}";
                    }
                    if (error is not null)
                    {
                        yield return error;
                    }
                    break;
            }
        }
    }

    [LoggerMessage(LogLevel.Debug, "Adding section for country: {country}.")]
    static partial void LogAddingSectionForCountry(ILogger<PlonkItGuideVectorStore> logger, string country);

    [LoggerMessage(LogLevel.Error, "Could not create PlonkIt Guide vector store because not all connections could be established.")]
    static partial void LogConnectionsUnavailable(ILogger<PlonkItGuideVectorStore> logger);

    [LoggerMessage(LogLevel.Debug, "{StatusUpdate}")]
    static partial void LogStoreInitStatus(ILogger<PlonkItGuideVectorStore> logger, string statusUpdate);

    [LoggerMessage(LogLevel.Debug, "Initializing PlonkIt Guide done.")]
    static partial void LogStoreInitDone(ILogger<PlonkItGuideVectorStore> logger);
}
