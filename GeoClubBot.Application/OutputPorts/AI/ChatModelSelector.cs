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
    /// Builds the model chain for one request: the best vetted candidates in priority order. The caller
    /// sends the first entry as <c>model</c> and the whole list as <c>models</c>, letting the provider
    /// fail over server-side.
    ///
    /// <see cref="ChatModelSelectionOptions.FallbackModelId"/> only fills slots no vetted model can.
    /// It used to end every chain, and every answer from a safety classifier or a 2B model in the
    /// beta-test feedback came through it after the ranked models had failed upstream: it picks at
    /// random and cannot be told to avoid anything. So it is appended only when the roster runs short,
    /// and never once it has been tried — which means a retry's chain can come back empty.
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

        // ChainLength counts the whole chain, because providers cap how many models a single request
        // may name — OpenRouter rejects more than three outright — so the number that reaches the wire
        // is the one that has to be configurable.
        var slots = Math.Max(1, options.ChainLength);

        var chain = ranked
            .Take(slots)
            .Select(candidate => candidate.Id)
            .ToList();

        if (chain.Count < slots && !IsExcluded(options.FallbackModelId, BuildExclusions(requirements)))
        {
            chain.Add(options.FallbackModelId);
        }

        return chain;
    }

    /// <summary>
    /// Whether a model must never be trusted with an answer, however it came to be asked. Eligibility
    /// applies the same rules before asking; this exists for the router's picks, which bypass
    /// eligibility entirely, so its answers can be screened after the fact.
    /// </summary>
    /// <param name="model">The roster's entry for <paramref name="modelId"/>, or null when it has none.</param>
    public static bool IsUnfitToAnswer(string modelId, ChatModelDescriptor? model, ChatModelSelectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.BlockedModelIds.Contains(modelId))
        {
            return true;
        }

        // A model the roster does not know is taken at face value: refusing every unfamiliar id would
        // throw away good answers whenever the roster is stale.
        if (model is null)
        {
            return false;
        }

        // A model that emits anything besides text is a generator, not a chat model. Providers list
        // music and image generators on the same roster; they are billed per second or per picture
        // rather than per token, so both token prices read "0", they rank well on context, and they
        // then reject a chat completion outright. Every genuine chat model emits text and nothing else.
        //
        // A safety classifier answers with a verdict on the message rather than a reply to it, so a
        // question routed to one comes back as "User Safety: safe". It is a valid completion, the
        // request succeeds, no failure is recorded, and the model keeps its rank — which is why this
        // has to be a rule rather than something the failure tracker could learn.
        return !model.ProducesTextOnly || model.IsGuardrail;
    }

    /// <summary>
    /// Works out which models of a chain failed upstream, from the one that finally answered.
    ///
    /// The provider tries the chain in order and reports only the model that answered, so every entry
    /// before it was skipped for failing — rate-limited, down, or refusing. Demoting those is what lets
    /// the next question start with a model that is working instead of failing over the same way again.
    /// Blame is withheld whenever the answering id cannot be placed: a provider that reports a model
    /// under a slightly different name must never cost the model that actually answered its rank.
    /// </summary>
    /// <param name="answeredBy">The model that answered, or null when the whole chain failed.</param>
    public static IReadOnlyList<string> InferFailedModels(
        IReadOnlyList<string> chain,
        string? answeredBy,
        string fallbackModelId,
        IEnumerable<ChatModelDescriptor> roster)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(roster);

        // The router is never blamed: it is not ranked, so a penalty on it would change nothing.
        var ranked = chain
            .Where(id => !string.Equals(id, fallbackModelId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (answeredBy is null)
        {
            return ranked;
        }

        for (var index = 0; index < chain.Count; index++)
        {
            if (string.Equals(chain[index], answeredBy, StringComparison.OrdinalIgnoreCase))
            {
                return [.. chain.Take(index)
                    .Where(id => !string.Equals(id, fallbackModelId, StringComparison.OrdinalIgnoreCase))];
            }
        }

        // An answer from outside the chain can only be the router's pick, and only a model the roster
        // knows is certainly a different model rather than one of ours reported under another name.
        var routerPicked = ranked.Count < chain.Count
                           && roster.Any(model => string.Equals(model.Id, answeredBy, StringComparison.OrdinalIgnoreCase));

        return routerPicked ? ranked : [];
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

        var excluded = BuildExclusions(requirements);

        return roster
            .Where(model => IsEligible(model, requirements, excluded, options, nowUtc))
            .Select(model => Score(model, options, failurePenalties, nowUtc))
            // Ties broken by id so the ordering is stable across runs and across test invocations.
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Rebuilt with a known comparer rather than trusted as passed: a retry that excluded "A/Model"
    /// must not be offered "a/model" again because the caller built its set case-sensitively.
    /// </summary>
    private static HashSet<string>? BuildExclusions(ChatModelRequirements requirements) =>
        requirements.ExcludedModelIds is { Count: > 0 } ids
            ? new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase)
            : null;

    private static bool IsExcluded(string modelId, HashSet<string>? excluded) =>
        excluded?.Contains(modelId) == true;

    private static bool IsEligible(
        ChatModelDescriptor model,
        ChatModelRequirements requirements,
        HashSet<string>? excluded,
        ChatModelSelectionOptions options,
        DateTimeOffset nowUtc)
    {
        // Blocked, generators and safety classifiers — the same rules the router's picks are screened by.
        if (IsUnfitToAnswer(model.Id, model, options))
        {
            return false;
        }

        // The fallback router is appended when the ranking runs short; ranking it too would let it
        // take a slot a real candidate should have.
        if (string.Equals(model.Id, options.FallbackModelId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Already tried for this question: a retry exists to ask someone else.
        if (IsExcluded(model.Id, excluded))
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
