using Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI;

namespace UseCases.UseCases.AI.Retrieval;

/// <summary>
/// The index's own country names as a <see cref="CountryGazetteer"/>, read once and kept.
///
/// Reading them scrolls every point in the index — about 28,600, 112 pages — so it is done at most
/// once per <see cref="AiCountryBoostConfiguration.CountryListCacheHours"/>, not per question. The
/// names change only when a source of a new country is indexed. A singleton for that reason.
///
/// Never fails a question: when the names cannot be read, the last ones read are kept, or no country
/// is boosted until the next attempt a minute later.
/// </summary>
public sealed partial class KnowledgeCountryDirectory(
    IKnowledgeIndex knowledgeIndex,
    IOptions<AiRetrievalConfiguration> configuration,
    TimeProvider timeProvider,
    ILogger<KnowledgeCountryDirectory> logger)
{
    /// <summary>How long a failed read is remembered before the index is asked again.</summary>
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _refresh = new(1, 1);

    private volatile Snapshot? _snapshot;

    public async Task<CountryGazetteer> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (IsFresh(_snapshot) is { } fresh)
        {
            return fresh;
        }

        await _refresh.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Another question may have read them while this one waited.
            if (IsFresh(_snapshot) is { } refreshed)
            {
                return refreshed;
            }

            var settings = configuration.Value.CountryBoost;
            var now = timeProvider.GetUtcNow();

            try
            {
                var names = await knowledgeIndex.ListCountriesAsync(cancellationToken).ConfigureAwait(false);
                var gazetteer = CountryGazetteer.Build(names, settings.Aliases);

                _snapshot = new Snapshot(gazetteer, now + TimeSpan.FromHours(settings.CountryListCacheHours));
                LogCountriesRead(logger, names.Count);
                return gazetteer;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                var kept = _snapshot?.Gazetteer ?? CountryGazetteer.Empty;
                _snapshot = new Snapshot(kept, now + RetryAfterFailure);
                LogCountriesUnavailable(logger, exception);
                return kept;
            }
        }
        finally
        {
            _refresh.Release();
        }
    }

    private CountryGazetteer? IsFresh(Snapshot? snapshot) =>
        snapshot is not null && timeProvider.GetUtcNow() < snapshot.ExpiresAt ? snapshot.Gazetteer : null;

    private sealed record Snapshot(CountryGazetteer Gazetteer, DateTimeOffset ExpiresAt);

    [LoggerMessage(LogLevel.Information, "Read {Count} country names from the guide index for country-aware retrieval.")]
    static partial void LogCountriesRead(ILogger logger, int count);

    [LoggerMessage(LogLevel.Warning,
        "Could not read the guide index's country names; questions are searched without a country boost for now.")]
    static partial void LogCountriesUnavailable(ILogger logger, Exception exception);
}
