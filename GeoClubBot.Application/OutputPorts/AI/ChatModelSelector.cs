namespace UseCases.OutputPorts.AI;

/// <summary>
/// Turns the provider's current free-model roster into an ordered fallback chain.
///
/// Free models churn constantly — they appear, get retired (<see cref="ChatModelDescriptor.ExpiresAt"/>),
/// and vary wildly in size and health — so pinning a single model id guarantees the feature breaks
/// silently. This selector is deliberately a pure function of (roster, requirements, options, health,
/// now) so the ranking can be unit-tested against a captured roster without any network.
/// </summary>
public static class ChatModelSelector
{
    // Weights sum to 1.0, so a raw score lands in [0, 1] before bonuses and penalties. Set against the
    // free roster of 2026-10-23: context used to carry the most weight although every candidate offered
    // 256k or more, so the order came down to recency and to a structured-output flag the bot never
    // uses — and the chain gave two of its three slots to models that refused every request. Size leads
    // now: asked the same grounded question that day, the largest model gave the best answer and the
    // smallest returned none at all.
    private const double SizeWeight = 0.40;
    private const double ExpiryWeight = 0.15;
    private const double UptimeWeight = 0.10;
    private const double RecencyWeight = 0.10;
    private const double ContextWeight = 0.10;
    private const double CapabilityWeight = 0.10;
    private const double CompletionWeight = 0.05;

    /// <summary>
    /// What a preferred prefix is worth over the one listed after it, and what the last one listed is
    /// worth over a model matching none. A whole raw-score range, so list order wins whatever the
    /// factors say: the operator wrote it down for a reason the roster does not show. Failures still
    /// win over it — see the penalty in <see cref="Score"/>.
    /// </summary>
    private const double PreferredBonusStep = 1.0;

    /// <summary>
    /// Enough to drop a coding specialist below every general model of similar size, without excluding
    /// it: when nothing else is left, a coding model still beats the router's random pick.
    /// </summary>
    private const double CodingSpecialistDemotion = 0.5;

    /// <summary>
    /// For the members a family itself calls small. Lighter than the coding demotion, because these are
    /// general models, only the least able of their family: Nemotron Lightning, asked a grounded
    /// question in October 2026, spent its whole 4,096-token allowance reasoning and returned no answer.
    /// </summary>
    private const double LightTierDemotion = 0.2;

    /// <summary>Whole words of an id or name by which a family marks its small members.</summary>
    private static readonly string[] LightTierWords = ["nano", "mini", "lightning", "flash"];

    /// <summary>
    /// The size factor climbs on a log scale from nothing at this many effective parameters (billions)
    /// to full marks at <see cref="SizeCeilingBillions"/>, so doubling a model is worth the same anywhere
    /// in between.
    /// </summary>
    private const double SizeFloorBillions = 8d;

    private const double SizeCeilingBillions = 256d;

    /// <summary>For a model whose size nobody states: neutral, because not saying is no evidence of being small.</summary>
    private const double UnknownSizeFactor = 0.5d;

    /// <summary>
    /// Context scores on a log scale from nothing at the smallest window a question may be asked in by
    /// default to full marks at <see cref="ContextSaturationTokens"/>.
    /// </summary>
    private const double ContextFloorTokens = 8_192d;

    /// <summary>
    /// Past this a longer window changes nothing — a question's prompt, history and excerpts fit many
    /// times over — so 1M must not outrank 256k.
    /// </summary>
    private const double ContextSaturationTokens = 131_072d;

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
    /// The models whose provider health is worth reading on a catalog refresh: the head of the text
    /// ranking and of the vision ranking, taken in turn, until <paramref name="limit"/> is reached.
    ///
    /// Health is read one model per request, through the same rate limiter questions queue behind, so
    /// it is spent where it can change a chain — on the models a first chain or its retry would name —
    /// rather than on the whole roster. Ranked as if nothing had been measured: a model the last check
    /// found unhealthy is exactly the one worth checking again.
    /// </summary>
    /// <param name="minContextLength">The smallest window a question may be asked in, as a request would require it.</param>
    public static IReadOnlyList<string> SelectHealthCheckCandidates(
        IEnumerable<ChatModelDescriptor> roster,
        ChatModelSelectionOptions options,
        int minContextLength,
        DateTimeOffset nowUtc,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(options);

        if (limit <= 0)
        {
            return [];
        }

        var unmeasured = roster.Select(model => model with { UptimeLastDayPercent = null }).ToList();

        IReadOnlyList<RankedChatModel>[] rankings =
        [
            Rank(unmeasured, new ChatModelRequirements(MinContextLength: minContextLength), options, null, nowUtc),
            Rank(unmeasured, new ChatModelRequirements(NeedsImageInput: true, MinContextLength: minContextLength),
                options, null, nowUtc)
        ];

        var candidates = new List<string>(limit);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var depth = rankings.Max(ranking => ranking.Count);

        for (var index = 0; index < depth && candidates.Count < limit; index++)
        {
            foreach (var ranking in rankings)
            {
                if (index < ranking.Count && candidates.Count < limit && seen.Add(ranking[index].Id))
                {
                    candidates.Add(ranking[index].Id);
                }
            }
        }

        return candidates;
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
    ///
    /// Models below the size or health bar are left out — unless no model of the roster that could
    /// serve this kind of request clears it, in which case the bar is dropped. Both bars rest on what
    /// providers state and on one day's uptime, and a request answered by the best of the small or flaky
    /// models is still better than one handed to the router, which picks among exactly those at random.
    /// Models already tried do not count towards that: a retry after the qualified models failed gets
    /// the router rather than a 2B model or one whose provider served nothing all day.
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

        // Tried models are left out only after the bar is settled: it asks what could serve this kind of
        // request at all, not what is left of it.
        var eligible = roster
            .Where(model => IsEligible(model, requirements, options, nowUtc))
            .ToList();

        var applyBar = eligible.Any(model => MeetsQualityBar(model, options));

        return eligible
            .Where(model => !IsExcluded(model.Id, excluded) && (!applyBar || MeetsQualityBar(model, options)))
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

    /// <summary>
    /// Whether a model is big and healthy enough to be asked while others are. What nobody states — a
    /// size missing from the id and the prose, a health read that failed — passes: a provider that
    /// stops publishing a field must not silently empty the roster.
    /// </summary>
    private static bool MeetsQualityBar(ChatModelDescriptor model, ChatModelSelectionOptions options) =>
        (model.EffectiveParametersBillions is not { } size || size >= options.MinEffectiveParametersBillions)
        && (model.UptimeLastDayPercent is not { } uptime || uptime >= options.MinUptimePercent);

    private static RankedChatModel Score(
        ChatModelDescriptor model,
        ChatModelSelectionOptions options,
        IReadOnlyDictionary<string, double>? failurePenalties,
        DateTimeOffset nowUtc)
    {
        var sizeFactor = model.EffectiveParametersBillions is { } size
            ? Clamp01(Math.Log2(size / SizeFloorBillions) / Math.Log2(SizeCeilingBillions / SizeFloorBillions))
            : UnknownSizeFactor;
        var expiryFactor = ExpiryFactor(model.ExpiresAt, nowUtc);
        // Unmeasured counts as healthy, as it does for the bar: the read is bounded, so most of the
        // roster is never measured, and a failed read says nothing about the model.
        var uptimeFactor = model.UptimeLastDayPercent is { } uptime ? Clamp01(uptime / 100d) : 1d;
        var recencyFactor = RecencyFactor(model.CreatedAt, nowUtc);
        var contextFactor = Clamp01(
            (Math.Log2(Math.Max(1, model.ContextLength)) - Math.Log2(ContextFloorTokens))
            / (Math.Log2(ContextSaturationTokens) - Math.Log2(ContextFloorTokens)));
        var capabilityFactor = CapabilityFactor(model);
        var completionFactor = model.MaxCompletionTokens is { } max
            ? Clamp01(max / ReferenceCompletionTokens)
            // Unknown is neutral rather than disqualifying: many providers simply omit the field.
            : 0.5d;

        var preferenceRank = PreferenceRank(model.Id, options.PreferredModelPrefixes);
        var preferredBonus = preferenceRank is { } rank
            ? PreferredBonusStep * (options.PreferredModelPrefixes.Count - rank)
            : 0d;

        var penalty = 0d;
        if (failurePenalties?.TryGetValue(model.Id, out var tracked) == true)
        {
            penalty = Clamp01(tracked);
        }

        // A failure costs a preferred model its preference in proportion, so the same failures leave
        // every preferred model where a single flat bonus used to: on its raw score once fully penalised.
        // Charged at face value, a bonus of two or more steps would outweigh any penalty, and a preferred
        // head failing every question would stay the head for every question.
        var penaltyCost = penalty * Math.Max(1d, preferredBonus);

        var demotion = Demotion(model);

        var score = (SizeWeight * sizeFactor)
                    + (ExpiryWeight * expiryFactor)
                    + (UptimeWeight * uptimeFactor)
                    + (RecencyWeight * recencyFactor)
                    + (ContextWeight * contextFactor)
                    + (CapabilityWeight * capabilityFactor)
                    + (CompletionWeight * completionFactor)
                    + preferredBonus
                    - penaltyCost
                    - demotion;

        return new RankedChatModel(model.Id, model, score, preferenceRank is not null, penalty, demotion);
    }

    /// <summary>
    /// Position of the first prefix the id starts with, or null when it matches none. The first match
    /// counts, so a model matching both <c>nvidia/</c> and a later, narrower prefix keeps the earlier
    /// standing the operator gave it.
    /// </summary>
    private static int? PreferenceRank(string modelId, IReadOnlyList<string> prefixes)
    {
        for (var index = 0; index < prefixes.Count; index++)
        {
            if (modelId.StartsWith(prefixes[index], StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return null;
    }

    private static double Demotion(ChatModelDescriptor model)
    {
        var demotion = 0d;
        if (model.IsCodingSpecialist)
        {
            demotion += CodingSpecialistDemotion;
        }

        if (IsLightTier(model))
        {
            demotion += LightTierDemotion;
        }

        return demotion;
    }

    /// <summary>
    /// Matched on whole words, so <c>minimax/minimax-m2</c> is not mistaken for a "mini" model.
    /// </summary>
    private static bool IsLightTier(ChatModelDescriptor model) =>
        Words(model.Id).Concat(Words(model.Name))
            .Any(word => LightTierWords.Contains(word, StringComparer.OrdinalIgnoreCase));

    private static IEnumerable<string> Words(string text)
    {
        var start = -1;
        for (var index = 0; index <= text.Length; index++)
        {
            var isWordChar = index < text.Length && char.IsLetterOrDigit(text[index]);
            if (isWordChar && start < 0)
            {
                start = index;
            }
            else if (!isWordChar && start >= 0)
            {
                yield return text[start..index];
                start = -1;
            }
        }
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

    /// <summary>
    /// Headroom beyond the current request's needs, which filter on their own: a model that can also
    /// call tools and read pictures stays usable when the next request wants more. Structured output is
    /// no longer counted — the bot never asks for it, and rewarding it is what had a refusing model
    /// outrank the best one on the roster.
    /// </summary>
    private static double CapabilityFactor(ChatModelDescriptor model)
    {
        var factor = 0d;
        if (model.SupportsTools)
        {
            factor += 0.6d;
        }

        if (model.SupportsImageInput)
        {
            factor += 0.4d;
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
/// <param name="Demotion">
/// Score taken off for what the model is rather than how it behaved — a coding specialist, a family's
/// small member — kept apart from <paramref name="FailurePenalty"/>, which decays.
/// </param>
public sealed record RankedChatModel(
    string Id,
    ChatModelDescriptor Model,
    double Score,
    bool IsPreferred,
    double FailurePenalty,
    double Demotion = 0d);
