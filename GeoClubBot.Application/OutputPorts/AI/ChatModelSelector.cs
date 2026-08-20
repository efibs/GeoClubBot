namespace UseCases.OutputPorts.AI;

/// <summary>
/// Turns the provider's current free-model roster into an ordered fallback chain.
///
/// Free models churn constantly — they appear, get retired (<see cref="ChatModelDescriptor.ExpiresAt"/>),
/// and vary wildly in context size — so pinning a single model id guarantees the feature breaks
/// silently. This selector is deliberately a pure function of (roster, requirements, options, health,
/// now) so the ranking can be unit-tested against a captured roster without any network.
/// </summary>
public static class ChatModelSelector
{
    // Weights sum to 1.0, so a raw score lands in [0, 1] before bonuses and penalties.
    private const double ContextWeight = 0.35;
    private const double ExpiryWeight = 0.20;
    private const double CapabilityWeight = 0.20;
    private const double RecencyWeight = 0.15;
    private const double CompletionWeight = 0.10;

    /// <summary>A preferred model outranks every non-preferred one, whatever the raw score says.</summary>
    private const double PreferredBonus = 1.0;

    private const double ReferenceContextLength = 1_000_000d;
    private const double ReferenceCompletionTokens = 32_768d;

    /// <summary>Below this age a free model is still settling in and tends to be flaky.</summary>
    private static readonly TimeSpan RecencyRampUp = TimeSpan.FromDays(14);

    /// <summary>Past this age a model starts losing ground to newer releases.</summary>
    private static readonly TimeSpan RecencyPlateauEnd = TimeSpan.FromDays(120);

    private static readonly TimeSpan RecencyFloorAt = TimeSpan.FromDays(365);
    private const double RecencyFloor = 0.3;

    /// <summary>Expiry scores linearly up to this far out, so "retires next week" ranks below "no end date".</summary>
    private static readonly TimeSpan ExpiryComfortHorizon = TimeSpan.FromDays(60);

    /// <summary>
    /// Builds the model chain for one request: the best candidates in priority order, always ending
    /// with <see cref="ChatModelSelectionOptions.FallbackModelId"/>. The caller sends the first entry
    /// as <c>model</c> and the whole list as <c>models</c>, letting the provider fail over server-side.
    /// </summary>
    /// <param name="failurePenalties">
    /// Per-model penalty in [0, 1] from recent upstream failures, so a model that just errored is
    /// demoted without being permanently blocked. Missing entries mean "healthy".
    /// </param>
    public static IReadOnlyList<string> SelectChain(
        IEnumerable<ChatModelDescriptor> roster,
        ChatModelRequirements requirements,
        ChatModelSelectionOptions options,
        IReadOnlyDictionary<string, double>? failurePenalties,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(options);

        var ranked = Rank(roster, requirements, options, failurePenalties, nowUtc);

        var chain = ranked
            .Take(Math.Max(0, options.ChainLength))
            .Select(candidate => candidate.Id)
            .ToList();

        // The fallback router is always reachable, and must never appear twice.
        chain.RemoveAll(id => string.Equals(id, options.FallbackModelId, StringComparison.OrdinalIgnoreCase));
        chain.Add(options.FallbackModelId);

        return chain;
    }

    /// <summary>
    /// Scores and orders the eligible models, best first. Exposed separately from
    /// <see cref="SelectChain"/> so <c>/ai models</c> can show the ranking with its reasoning.
    /// </summary>
    public static IReadOnlyList<RankedChatModel> Rank(
        IEnumerable<ChatModelDescriptor> roster,
        ChatModelRequirements requirements,
        ChatModelSelectionOptions options,
        IReadOnlyDictionary<string, double>? failurePenalties,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(options);

        return roster
            .Where(model => IsEligible(model, requirements, options, nowUtc))
            .Select(model => Score(model, options, failurePenalties, nowUtc))
            // Ties broken by id so the ordering is stable across runs and across test invocations.
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsEligible(
        ChatModelDescriptor model,
        ChatModelRequirements requirements,
        ChatModelSelectionOptions options,
        DateTimeOffset nowUtc)
    {
        if (options.BlockedModelIds.Contains(model.Id))
        {
            return false;
        }

        // The fallback router is appended unconditionally; ranking it too would let it win a slot
        // it is already guaranteed, pushing out a real candidate.
        if (string.Equals(model.Id, options.FallbackModelId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (requirements.NeedsImageInput && !model.SupportsImageInput)
        {
            return false;
        }

        if (requirements.NeedsTools && !model.SupportsTools)
        {
            return false;
        }

        if (model.ContextLength < requirements.MinContextLength)
        {
            return false;
        }

        // Never pick a model that retires before we could reasonably notice and re-rank.
        return model.ExpiresAt is null || model.ExpiresAt.Value - nowUtc > options.ExpiryHorizon;
    }

    private static RankedChatModel Score(
        ChatModelDescriptor model,
        ChatModelSelectionOptions options,
        IReadOnlyDictionary<string, double>? failurePenalties,
        DateTimeOffset nowUtc)
    {
        var contextFactor = Clamp01(Math.Log2(Math.Max(1, model.ContextLength)) / Math.Log2(ReferenceContextLength));
        var expiryFactor = ExpiryFactor(model.ExpiresAt, nowUtc);
        var capabilityFactor = CapabilityFactor(model);
        var recencyFactor = RecencyFactor(model.CreatedAt, nowUtc);
        var completionFactor = model.MaxCompletionTokens is { } max
            ? Clamp01(max / ReferenceCompletionTokens)
            // Unknown is neutral rather than disqualifying: many providers simply omit the field.
            : 0.5d;

        var isPreferred = options.PreferredModelPrefixes
            .Any(prefix => model.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        var penalty = 0d;
        if (failurePenalties?.TryGetValue(model.Id, out var tracked) == true)
        {
            penalty = Clamp01(tracked);
        }

        var score = (ContextWeight * contextFactor)
                    + (ExpiryWeight * expiryFactor)
                    + (CapabilityWeight * capabilityFactor)
                    + (RecencyWeight * recencyFactor)
                    + (CompletionWeight * completionFactor)
                    + (isPreferred ? PreferredBonus : 0d)
                    - penalty;

        return new RankedChatModel(model.Id, model, score, isPreferred, penalty);
    }

    private static double ExpiryFactor(DateTimeOffset? expiresAt, DateTimeOffset nowUtc)
    {
        if (expiresAt is null)
        {
            return 1d;
        }

        var remaining = expiresAt.Value - nowUtc;
        return remaining <= TimeSpan.Zero
            ? 0d
            : Clamp01(remaining / ExpiryComfortHorizon);
    }

    private static double CapabilityFactor(ChatModelDescriptor model)
    {
        // Headroom beyond the current request's needs: a model that can also do tools, structured
        // output and vision stays usable when the next request wants more.
        var factor = 0d;
        if (model.SupportsTools)
        {
            factor += 0.4d;
        }

        if (model.SupportsStructuredOutputs)
        {
            factor += 0.3d;
        }

        if (model.SupportsImageInput)
        {
            factor += 0.3d;
        }

        return factor;
    }

    private static double RecencyFactor(DateTimeOffset? createdAt, DateTimeOffset nowUtc)
    {
        if (createdAt is null)
        {
            return 0.5d;
        }

        var age = nowUtc - createdAt.Value;
        if (age < TimeSpan.Zero)
        {
            return 0.5d;
        }

        if (age <= RecencyRampUp)
        {
            // Brand-new free models are frequently unstable or capacity-starved; ramp them in.
            return Clamp01(age / RecencyRampUp);
        }

        if (age <= RecencyPlateauEnd)
        {
            return 1d;
        }

        if (age >= RecencyFloorAt)
        {
            return RecencyFloor;
        }

        var decayProgress = (age - RecencyPlateauEnd) / (RecencyFloorAt - RecencyPlateauEnd);
        return 1d - ((1d - RecencyFloor) * Clamp01(decayProgress));
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0d, 1d);
}

/// <summary>A scored candidate, carrying enough detail for <c>/ai models</c> to explain the ranking.</summary>
public sealed record RankedChatModel(
    string Id,
    ChatModelDescriptor Model,
    double Score,
    bool IsPreferred,
    double FailurePenalty);
