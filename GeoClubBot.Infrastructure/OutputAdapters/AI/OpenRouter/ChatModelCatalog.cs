using System.Collections.Concurrent;
using Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI;
using Utilities;

namespace Infrastructure.OutputAdapters.AI.OpenRouter;

/// <summary>
/// Singleton cache over the provider's free-model roster.
///
/// Registered as a singleton because the roster is process-wide state that outlives any request, and
/// because the failure tracker only demotes a flaky model usefully if it accumulates across turns.
/// </summary>
public partial class ChatModelCatalog(
    IChatModelClient client,
    IOptions<AiConfiguration> configuration,
    TimeProvider timeProvider,
    ILogger<ChatModelCatalog> logger) : IChatModelCatalog
{
    /// <summary>A failure costs this much score, decaying linearly to nothing over <see cref="FailureDecay"/>.</summary>
    private const double FailurePenaltyPerFailure = 0.35d;

    private static readonly TimeSpan FailureDecay = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<string, FailureRecord> _failures = new(StringComparer.OrdinalIgnoreCase);

    // Swapped atomically on refresh so readers never observe a partially-populated roster.
    private volatile RosterSnapshot _roster = RosterSnapshot.Empty;

    public async Task<Result<int>> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var result = await client.ReadFreeModelsAsync(cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            // Keep serving the previous roster: a stale list still beats falling back to the router
            // for every request, and completions may well still work.
            LogRefreshFailed(logger, result.Error.Message, _roster.Models.Count);
            return result.Error;
        }

        var models = result.Value;
        _roster = new RosterSnapshot(models, timeProvider.GetUtcNow(), AiCatalogSource.Live);

        LogRefreshed(logger, models.Count, models.Count(model => model.SupportsImageInput));
        return models.Count;
    }

    public Task<IReadOnlyList<string>> ReadChainAsync(
        ChatModelRequirements requirements,
        CancellationToken cancellationToken = default)
    {
        var options = BuildSelectionOptions();
        var chain = ChatModelSelector.SelectChain(
            _roster.Models,
            requirements,
            options,
            SnapshotFailurePenalties(),
            timeProvider.GetUtcNow());

        return Task.FromResult(chain);
    }

    public Task<IReadOnlyList<RankedChatModel>> ReadRankingAsync(
        ChatModelRequirements requirements,
        CancellationToken cancellationToken = default)
    {
        var ranking = ChatModelSelector.Rank(
            _roster.Models,
            requirements,
            BuildSelectionOptions(),
            SnapshotFailurePenalties(),
            timeProvider.GetUtcNow());

        return Task.FromResult(ranking);
    }

    public void ReportFailure(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        _failures.AddOrUpdate(
            modelId,
            _ => new FailureRecord(1, now),
            (_, existing) =>
            {
                // Count from scratch once the previous failures have fully decayed, so a model that
                // misbehaved last week does not stay demoted forever.
                var decayed = now - existing.LastFailureUtc >= FailureDecay;
                return new FailureRecord(decayed ? 1 : existing.Count + 1, now);
            });

        LogModelDemoted(logger, modelId);
    }

    public AiCatalogStatus ReadStatus()
    {
        var roster = _roster;
        return new AiCatalogStatus(
            roster.Models.Count,
            roster.Models.Count(model => model.SupportsImageInput),
            roster.RefreshedAtUtc,
            roster.Source);
    }

    private ChatModelSelectionOptions BuildSelectionOptions()
    {
        var openRouter = configuration.Value.OpenRouter;
        return new ChatModelSelectionOptions
        {
            PreferredModelPrefixes = openRouter.PreferredModelPrefixes,
            BlockedModelIds = openRouter.BlockedModelIds.ToHashSet(StringComparer.OrdinalIgnoreCase),
            FallbackModelId = openRouter.FallbackModelId,
            ChainLength = openRouter.ChainLength,
            ExpiryHorizon = TimeSpan.FromHours(openRouter.ExpiryHorizonHours)
        };
    }

    private Dictionary<string, double> SnapshotFailurePenalties()
    {
        var now = timeProvider.GetUtcNow();
        var penalties = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var (modelId, record) in _failures)
        {
            var age = now - record.LastFailureUtc;
            if (age >= FailureDecay)
            {
                _failures.TryRemove(modelId, out _);
                continue;
            }

            var remaining = 1d - (age / FailureDecay);
            penalties[modelId] = Math.Clamp(record.Count * FailurePenaltyPerFailure * remaining, 0d, 1d);
        }

        return penalties;
    }

    private sealed record FailureRecord(int Count, DateTimeOffset LastFailureUtc);

    private sealed record RosterSnapshot(
        IReadOnlyList<ChatModelDescriptor> Models,
        DateTimeOffset? RefreshedAtUtc,
        AiCatalogSource Source)
    {
        public static readonly RosterSnapshot Empty = new([], null, AiCatalogSource.None);
    }

    [LoggerMessage(LogLevel.Information, "Refreshed AI model catalog: {ModelCount} free model(s), {VisionCount} accepting images.")]
    static partial void LogRefreshed(ILogger logger, int modelCount, int visionCount);

    [LoggerMessage(LogLevel.Warning, "Could not refresh the AI model catalog ({Reason}); continuing with {ModelCount} cached model(s).")]
    static partial void LogRefreshFailed(ILogger logger, string reason, int modelCount);

    [LoggerMessage(LogLevel.Information, "Demoted AI model {ModelId} after an upstream failure.")]
    static partial void LogModelDemoted(ILogger logger, string modelId);
}
