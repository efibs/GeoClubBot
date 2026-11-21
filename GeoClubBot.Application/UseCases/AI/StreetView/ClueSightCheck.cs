using Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Conversations;
using Utilities;

namespace UseCases.UseCases.AI.StreetView;

/// <summary>One place to look at: its panorama, the views to read of it, and the guides' clues for it.</summary>
/// <param name="PanoId">Google's panorama id.</param>
/// <param name="Angles">The views to read (<see cref="StreetViewAngles"/>).</param>
/// <param name="CountryName">Where it is.</param>
/// <param name="Region">Its first-level region, when known.</param>
/// <param name="GuessedCountryName">The country the player guessed instead, for a round they missed.</param>
/// <param name="Candidates">The guides' clues for the place, the most reliable first.</param>
public sealed record SightRequest(
    string PanoId,
    IReadOnlyList<StreetViewAngle> Angles,
    string CountryName,
    string? Region,
    string? GuessedCountryName,
    IReadOnlyList<KnowledgeHit> Candidates);

/// <summary>What was made of one place.</summary>
/// <param name="Pictures">The views read, in the order asked; empty when Google served none.</param>
/// <param name="Verdict">What the vision model saw; null when no model looked (none free, the allowance spent, every reply unusable).</param>
/// <param name="Model">The model that looked.</param>
public sealed record PlaceSight(IReadOnlyList<StreetViewPicture> Pictures, ClueSightVerdict? Verdict, string? Model)
{
    /// <summary>A model looked, and said something usable.</summary>
    public bool Checked => Verdict is { IsUsable: true };

    /// <summary>The picture to show for the place: the one the model found most telling, else the first.</summary>
    public StreetViewPicture? BestPicture =>
        Pictures.Count == 0 ? null : Pictures[Math.Clamp(Verdict?.BestPicture ?? 0, 0, Pictures.Count - 1)];
}

/// <summary>
/// Looks at Street View places the way the player saw them and says which of the guides' clues each
/// shows: the views read free from Google, then one request per place to a free vision model
/// (<see cref="ClueSightPrompt"/>). The places are looked at side by side; their requests are claimed
/// from the day's allowance together, before any is made, and those not spent are given back, because the
/// allowance is kept in the one database context, which is no place for parallel writes. A request that
/// fails is retried once against the models not yet asked, if a spare request was claimed for it. Never
/// fails: a place that could not be looked at comes back unchecked, or null when Google served no view.
/// </summary>
public sealed partial class ClueSightCheck(
    IStreetViewImagery imagery,
    IChatModelClient chatClient,
    IChatModelCatalog modelCatalog,
    IAiBudgetRepository budget,
    IOptions<AiConfiguration> aiConfiguration,
    IOptions<AiRoundReviewConfiguration> reviewConfiguration,
    ILogger<ClueSightCheck> logger)
{
    // No token cap, as for an answer: the free vision models include reasoning ones (Nemotron Nano Omni),
    // which spent a cap of 2,000 on thinking and returned nothing (2026-11-21). The deadline bounds them.

    /// <summary>Attempts per place: the first chain, then one retry against untried models.</summary>
    private const int MaxAttempts = 2;

    /// <summary>Spare requests claimed for retries, shared by the places of one call.</summary>
    private const int SpareRequests = 2;

    /// <param name="budgetDay">The day the requests count against.</param>
    /// <returns>One entry per request, in order: null when Google served no view of that place.</returns>
    public async Task<IReadOnlyList<PlaceSight?>> LookAsync(
        IReadOnlyList<SightRequest> requests,
        DateOnly budgetDay,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0)
        {
            return [];
        }

        var settings = reviewConfiguration.Value.SightCheck;
        var pictures = await Task.WhenAll(requests.Select(request => imagery.ReadViewsAsync(request.PanoId, request.Angles, cancellationToken)))
            .ConfigureAwait(false);

        var readable = Enumerable.Range(0, requests.Count).Where(index => pictures[index].Count > 0).ToList();
        if (!settings.Enabled || readable.Count == 0)
        {
            return Unchecked(pictures);
        }

        var ai = aiConfiguration.Value;
        var spare = Math.Min(SpareRequests, readable.Count);
        var claimed = readable.Count + spare;
        if (!await budget.TryReserveRequestsAsync(budgetDay, claimed, ai.OpenRouter.DailyRequestBudget, cancellationToken).ConfigureAwait(false))
        {
            spare = 0;
            claimed = readable.Count;
            if (!await budget.TryReserveRequestsAsync(budgetDay, claimed, ai.OpenRouter.DailyRequestBudget, cancellationToken).ConfigureAwait(false))
            {
                LogBudgetShort(logger, readable.Count);
                return Unchecked(pictures);
            }
        }

        var chain = await modelCatalog.ReadChainAsync(
                new ChatModelRequirements(NeedsImageInput: true, MinContextLength: ai.OpenRouter.MinContextLength),
                cancellationToken)
            .ConfigureAwait(false);
        if (chain.Count == 0)
        {
            LogNoModel(logger);
            await budget.ReleaseRequestsAsync(budgetDay, claimed, cancellationToken).ConfigureAwait(false);
            return Unchecked(pictures);
        }

        var spares = new SpareCounter(spare);
        var outcomes = await Task.WhenAll(readable.Select(index =>
                AskAsync(requests[index], pictures[index], chain, spares, settings, cancellationToken)))
            .ConfigureAwait(false);

        var used = outcomes.Sum(outcome => outcome.Requests);
        if (claimed > used)
        {
            await budget.ReleaseRequestsAsync(budgetDay, claimed - used, cancellationToken).ConfigureAwait(false);
        }

        var promptTokens = outcomes.Sum(outcome => outcome.Usage?.PromptTokens ?? 0);
        var completionTokens = outcomes.Sum(outcome => outcome.Usage?.CompletionTokens ?? 0);
        if (promptTokens + completionTokens > 0)
        {
            await budget.RecordTokenUsageAsync(budgetDay, promptTokens, completionTokens, cancellationToken).ConfigureAwait(false);
        }

        var sights = new PlaceSight?[requests.Count];
        for (var index = 0; index < requests.Count; index++)
        {
            sights[index] = pictures[index].Count == 0 ? null : new PlaceSight(pictures[index], null, null);
        }

        for (var position = 0; position < readable.Count; position++)
        {
            var index = readable[position];
            sights[index] = new PlaceSight(pictures[index], outcomes[position].Verdict, outcomes[position].Model);
        }

        LogLooked(logger, readable.Count, outcomes.Count(outcome => outcome.Verdict is { IsUsable: true }),
            outcomes.Sum(outcome => outcome.Verdict?.Seen.Count ?? 0), used);
        return sights;
    }

    private async Task<AskOutcome> AskAsync(
        SightRequest request,
        IReadOnlyList<StreetViewPicture> pictures,
        IReadOnlyList<string> chain,
        SpareCounter spares,
        AiClueSightConfiguration settings,
        CancellationToken cancellationToken)
    {
        var messages = ClueSightPrompt.BuildMessages(
            [.. pictures.Select(DataUrl)],
            [.. pictures.Select(picture => picture.Angle)],
            request.CountryName,
            request.Region,
            request.GuessedCountryName,
            request.Candidates);

        var requests = 0;
        var tried = new HashSet<string>(StringComparer.Ordinal);
        AiTokenUsage? usage = null;
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            if (attempt > 0)
            {
                if (!spares.TryTake())
                {
                    break;
                }

                chain = [.. (await modelCatalog.ReadChainAsync(
                            new ChatModelRequirements(NeedsImageInput: true, MinContextLength: aiConfiguration.Value.OpenRouter.MinContextLength),
                            cancellationToken)
                        .ConfigureAwait(false))
                    .Where(model => !tried.Contains(model))];
                if (chain.Count == 0)
                {
                    spares.GiveBack();
                    break;
                }
            }

            requests++;
            tried.UnionWith(chain);
            var completion = await CompleteAsync(new AiChatRequest(chain, messages, ClueSightPrompt.Temperature), settings, cancellationToken)
                .ConfigureAwait(false);
            if (completion.IsFailure)
            {
                LogFailed(logger, request.PanoId, chain[0], completion.Error.Code, completion.Error.Message);
                modelCatalog.ReportFailedRequest(chain, completion.Error);
                continue;
            }

            var response = completion.Value;
            usage = usage is null
                ? response.Usage
                : new AiTokenUsage(usage.PromptTokens + response.Usage.PromptTokens, usage.CompletionTokens + response.Usage.CompletionTokens);
            modelCatalog.ReportChainOutcome(chain, response.ModelUsed);
            tried.Add(response.ModelUsed);

            if (modelCatalog.IsUnfitToAnswer(response.ModelUsed) || GuardrailVerdictDetector.IsVerdict(response.Text))
            {
                LogUnusable(logger, request.PanoId, response.ModelUsed, "the model is one that must not answer");
                continue;
            }

            var verdict = ClueSightPrompt.Parse(response.Text, request.Candidates.Count, pictures.Count);
            if (!verdict.IsUsable)
            {
                LogUnusable(logger, request.PanoId, response.ModelUsed, Shorten(response.Text));
                continue;
            }

            LogChecked(logger, request.PanoId, response.ModelUsed, verdict.Seen.Count, request.Candidates.Count, verdict.AlsoSeen.Count);
            return new AskOutcome(verdict, response.ModelUsed, requests, usage);
        }

        return new AskOutcome(null, null, requests, usage);
    }

    private async Task<Result<AiChatResponse>> CompleteAsync(AiChatRequest request, AiClueSightConfiguration settings, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, settings.TimeoutSeconds)));
        try
        {
            return await chatClient.CompleteAsync(request, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Error.Unexpected(ChatErrorCodes.TimedOut, "The model did not look at the pictures in time.");
        }
    }

    private static PlaceSight?[] Unchecked(IReadOnlyList<StreetViewPicture>[] pictures) =>
        [.. pictures.Select(views => views.Count == 0 ? null : new PlaceSight(views, null, null))];

    private static string DataUrl(StreetViewPicture picture) =>
        $"data:{picture.ContentType};base64,{Convert.ToBase64String(picture.Content)}";

    private static string Shorten(string? text) =>
        string.IsNullOrEmpty(text) ? "(nothing)" : text.Length <= 160 ? text : text[..160] + "…";

    private sealed record AskOutcome(ClueSightVerdict? Verdict, string? Model, int Requests, AiTokenUsage? Usage);

    /// <summary>The spare requests of one call, taken by whichever place needs a retry first.</summary>
    private sealed class SpareCounter(int count)
    {
        private int _left = count;

        public bool TryTake()
        {
            while (true)
            {
                var left = Volatile.Read(ref _left);
                if (left <= 0)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref _left, left - 1, left) == left)
                {
                    return true;
                }
            }
        }

        public void GiveBack() => Interlocked.Increment(ref _left);
    }

    [LoggerMessage(LogLevel.Information, "Looked at {Places} Street View place(s): {Checked} checked, {Seen} clue(s) seen, {Requests} request(s) spent.")]
    static partial void LogLooked(ILogger logger, int places, int @checked, int seen, int requests);

    [LoggerMessage(LogLevel.Information, "{Model} looked at panorama {PanoId}: {Seen} of {Candidates} clue(s) seen, {Notes} other note(s).")]
    static partial void LogChecked(ILogger logger, string panoId, string model, int seen, int candidates, int notes);

    [LoggerMessage(LogLevel.Warning, "The vision request for panorama {PanoId} failed on {Model} ({Code}: {Message}).")]
    static partial void LogFailed(ILogger logger, string panoId, string model, string code, string message);

    [LoggerMessage(LogLevel.Warning, "{Model} gave no usable reading of panorama {PanoId}: {Reply}")]
    static partial void LogUnusable(ILogger logger, string panoId, string model, string reply);

    [LoggerMessage(LogLevel.Warning, "The day's allowance cannot pay for looking at {Places} Street View place(s); they go unchecked.")]
    static partial void LogBudgetShort(ILogger logger, int places);

    [LoggerMessage(LogLevel.Warning, "No free model that reads pictures is available; the Street View places go unchecked.")]
    static partial void LogNoModel(ILogger logger);
}
