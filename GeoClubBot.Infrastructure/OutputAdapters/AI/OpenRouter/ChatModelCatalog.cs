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

    /// <summary>
    /// Most models whose health one refresh reads, whatever is configured: each read is a request through
    /// the limiter questions wait behind, and twenty already covers every model a first chain and its
    /// retry could name, for text and for pictures, several times over.
    /// </summary>
    public const int MaxHealthChecks = 20;

    private readonly ConcurrentDictionary<string, FailureRecord> _failures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Models that answered with something that was not an answer, or refused to serve this bot at all,
    /// kept out until restart. Held apart from the failure penalties because those decay within the
    /// hour — and a classifier the description rules missed would be back answering "User Safety: safe"
    /// as soon as they did.
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _learnedUnfit = new(StringComparer.OrdinalIgnoreCase);

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

        // Published before health is read, since that takes a request per model through the limiter and
        // questions asked meanwhile should already get today's roster. Until health has been read again,
        // each model keeps the figure last measured for it: a model down six hours ago is likelier down
        // than not, and the half-minute the reads take is no reason to offer it.
        var models = CarryOverHealth(result.Value, _roster.Models);
        var published = new RosterSnapshot(models, timeProvider.GetUtcNow(), AiCatalogSource.Live);
        _roster = published;

        LogRefreshed(logger, models.Count, models.Count(model => model.SupportsImageInput));

        await RefreshHealthAsync(published, cancellationToken).ConfigureAwait(false);
        return models.Count;
    }

    /// <summary>
    /// Reads the provider health of the models a chain could name and folds it into the roster.
    ///
    /// The roster lists a model whether or not anything behind it answers: on 2026-10-23 it offered
    /// Nemotron 3 Super, whose only provider had served 0% of the day's requests, and LFM 2.5 at 9%.
    /// The read is free — a public endpoint, no key, no chat allowance — but one request per model, so
    /// it is bounded to the head of the rankings. A read that fails leaves the model unmeasured, which
    /// the selector treats as healthy: an unreachable health endpoint must never empty the roster.
    /// </summary>
    private async Task RefreshHealthAsync(RosterSnapshot published, CancellationToken cancellationToken)
    {
        var openRouter = configuration.Value.OpenRouter;
        var limit = Math.Clamp(openRouter.HealthCheckModelCount, 0, MaxHealthChecks);
        if (limit == 0 || published.Models.Count == 0)
        {
            return;
        }

        var candidates = ChatModelSelector.SelectHealthCheckCandidates(
            published.Models,
            BuildSelectionOptions(),
            openRouter.MinContextLength,
            timeProvider.GetUtcNow(),
            limit);

        var measured = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);

        // One at a time: each read queues for a token, and a question arriving meanwhile then waits
        // behind at most one of them rather than behind the whole batch.
        foreach (var modelId in candidates)
        {
            var health = await client.ReadModelHealthAsync(modelId, cancellationToken).ConfigureAwait(false);
            measured[modelId] = health.IsSuccess ? health.Value.UptimeLastDayPercent : null;
        }

        // Exactly this pass's figures from here on. A model not checked this time, or whose read failed,
        // is unmeasured again rather than held to a figure that may be a day old.
        var models = published.Models
            .Select(model => model with
            {
                UptimeLastDayPercent = measured.TryGetValue(model.Id, out var uptime) ? uptime : null
            })
            .ToList();

        // A refresh that started later has already published a newer roster; it reads its own health.
        if (Interlocked.CompareExchange(ref _roster, published with { Models = models }, published) != published)
        {
            return;
        }

        var unhealthy = models
            .Where(model => model.UptimeLastDayPercent < openRouter.MinProviderUptimePercent)
            .Select(model => $"{model.Id} ({model.UptimeLastDayPercent:0.#}%)")
            .ToList();

        LogHealthRefreshed(
            logger,
            candidates.Count,
            measured.Count(entry => entry.Value is not null),
            unhealthy.Count == 0 ? "none" : string.Join(", ", unhealthy));
    }

    /// <summary>
    /// Copies each model's last measured health into a freshly read roster, which never carries any.
    /// A model that has left the roster takes its measurement with it.
    /// </summary>
    private static List<ChatModelDescriptor> CarryOverHealth(
        IReadOnlyList<ChatModelDescriptor> fresh,
        IReadOnlyList<ChatModelDescriptor> previous)
    {
        var lastMeasured = previous
            .Where(model => model.UptimeLastDayPercent is not null)
            .GroupBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().UptimeLastDayPercent, StringComparer.OrdinalIgnoreCase);

        return
        [
            .. fresh.Select(model => model.UptimeLastDayPercent is null && lastMeasured.TryGetValue(model.Id, out var uptime)
                ? model with { UptimeLastDayPercent = uptime }
                : model)
        ];
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

    public void ReportChainOutcome(IReadOnlyList<string> chain, string? answeredBy)
    {
        ArgumentNullException.ThrowIfNull(chain);

        var failed = ChatModelSelector.InferFailedModels(
            chain,
            answeredBy,
            configuration.Value.OpenRouter.FallbackModelId,
            _roster.Models);

        foreach (var modelId in failed)
        {
            ReportFailure(modelId);
        }
    }

    public bool IsUnfitToAnswer(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return false;
        }

        var known = _roster.Models.FirstOrDefault(model =>
            string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase));

        return ChatModelSelector.IsUnfitToAnswer(modelId, known, BuildSelectionOptions());
    }

    public void ReportUnfit(string modelId)
    {
        // The router is not a model and never ranked, so excluding it would change nothing but the log.
        if (string.IsNullOrWhiteSpace(modelId)
            || string.Equals(modelId, configuration.Value.OpenRouter.FallbackModelId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_learnedUnfit.TryAdd(modelId, 0))
        {
            LogModelExcluded(logger, modelId);
        }
    }

    public AiCatalogStatus ReadStatus()
    {
        var roster = _roster;
        return new AiCatalogStatus(
            roster.Models.Count,
            roster.Models.Count(model => model.SupportsImageInput),
            roster.RefreshedAtUtc,
            roster.Source,
            [.. _learnedUnfit.Keys.Order(StringComparer.OrdinalIgnoreCase)]);
    }

    private ChatModelSelectionOptions BuildSelectionOptions()
    {
        var openRouter = configuration.Value.OpenRouter;

        // Blank entries are how an environment override drops a shipped one: lists bind by index, so an
        // override can only replace an entry, never remove it. A blank prefix would otherwise prefer
        // every model, and one bound as null would throw on the first comparison.
        var blocked = openRouter.BlockedModelIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        blocked.UnionWith(_learnedUnfit.Keys);

        return new ChatModelSelectionOptions
        {
            PreferredModelPrefixes = [.. openRouter.PreferredModelPrefixes.Where(prefix => !string.IsNullOrWhiteSpace(prefix))],
            BlockedModelIds = blocked,
            FallbackModelId = openRouter.FallbackModelId,
            // Clamped to what one request may carry, so the chain a caller is handed — and later blames
            // or excludes on a retry — is exactly what went out, rather than something the client
            // silently trims.
            ChainLength = Math.Clamp(openRouter.ChainLength, 1, RefitChatModelClient.MaxModelsPerRequest),
            ExpiryHorizon = TimeSpan.FromHours(openRouter.ExpiryHorizonHours),
            MinEffectiveParametersBillions = Math.Max(0d, openRouter.MinEffectiveParametersBillions),
            MinUptimePercent = Math.Clamp(openRouter.MinProviderUptimePercent, 0d, 100d)
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

    [LoggerMessage(LogLevel.Information,
        "Read the provider health of {CheckedCount} AI model(s), {MeasuredCount} with a figure; below the uptime bar: {Unhealthy}.")]
    static partial void LogHealthRefreshed(ILogger logger, int checkedCount, int measuredCount, string unhealthy);

    [LoggerMessage(LogLevel.Warning,
        "AI model {ModelId} answered with something that is not an answer, or refused to serve this bot, and is excluded until restart. Add it to AI:OpenRouter:BlockedModelIds to keep it out for good.")]
    static partial void LogModelExcluded(ILogger logger, string modelId);
}
