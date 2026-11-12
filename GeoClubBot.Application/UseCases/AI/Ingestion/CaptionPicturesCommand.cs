using Configuration;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Conversations;
using Utilities;

namespace UseCases.UseCases.AI.Ingestion;

/// <summary>
/// Has a free vision model describe indexed guide pictures that have no words of their own, so a
/// written question can find them and an answer can say what they show.
/// </summary>
/// <param name="MaxPictures">Pictures to describe this run; null takes <c>AI:Captions:MaxPicturesPerRun</c>.</param>
public sealed record CaptionPicturesCommand(int? MaxPictures = null) : ICommand<Result<CaptionReport>>;

/// <param name="Attempted">Pictures a model was asked about.</param>
/// <param name="Described">Pictures that got a description, stored with the chunk.</param>
/// <param name="Unusable">Replies that were no description — a refusal, token soup, nothing — left for a later run.</param>
/// <param name="Failed">Requests that failed outright; the models were demoted and the pictures left for a later run.</param>
/// <param name="Remaining">Pictures still waiting for a description after this run.</param>
/// <param name="BudgetExhausted">The run stopped because describing has used its share of today's allowance.</param>
/// <param name="NoModel">No free model that reads pictures was available, so nothing was attempted.</param>
/// <param name="StoppedForFailures">
/// The run stopped early because every reply in a row was unusable or failed: the model is not
/// describing anything today, and the rest of the share is better left to answers.
/// </param>
/// <param name="RateLimited">The run stopped because the provider was still rate-limiting after its window had been waited out.</param>
public sealed record CaptionReport(
    int Attempted,
    int Described,
    int Unusable,
    int Failed,
    long Remaining,
    bool BudgetExhausted,
    bool NoModel = false,
    bool StoppedForFailures = false,
    bool RateLimited = false);

public sealed partial class CaptionPicturesHandler(
    IKnowledgeIndex knowledgeIndex,
    IChatModelClient chatClient,
    IChatModelCatalog modelCatalog,
    IEmbedder embedder,
    IImageRelay imageRelay,
    IAiBudgetRepository budget,
    IOptions<AiConfiguration> aiConfiguration,
    IOptions<AiCaptionsConfiguration> captionsConfiguration,
    TimeProvider timeProvider,
    ILogger<CaptionPicturesHandler> logger)
    : IRequestHandler<CaptionPicturesCommand, Result<CaptionReport>>
{
    public const string DisabledCode = "ai.captions_disabled";

    public async Task<Result<CaptionReport>> Handle(CaptionPicturesCommand request, CancellationToken cancellationToken)
    {
        var settings = captionsConfiguration.Value;
        if (!settings.Enabled)
        {
            return Error.Validation(DisabledCode, "Describing pictures is turned off (AI:Captions:Enabled).");
        }

        var limit = Math.Max(1, request.MaxPictures ?? settings.MaxPicturesPerRun);
        var version = Math.Max(1, settings.Version);

        var pictures = await knowledgeIndex.ReadPicturesWantingCaptionAsync(version, limit, cancellationToken)
            .ConfigureAwait(false);

        var run = new RunState();

        if (pictures.Count > 0)
        {
            await DescribeAsync(pictures, version, settings, run, cancellationToken).ConfigureAwait(false);
        }

        var remaining = await knowledgeIndex.CountPicturesWantingCaptionAsync(version, cancellationToken).ConfigureAwait(false);

        LogRun(logger, run.Attempted, run.Described, run.Unusable, run.Failed, remaining);

        return new CaptionReport(
            run.Attempted, run.Described, run.Unusable, run.Failed, remaining,
            run.BudgetExhausted, run.NoModel, run.StoppedForFailures, run.RateLimited);
    }

    /// <summary>
    /// Describes the pictures in groups of one embedding batch: each group's descriptions are embedded
    /// in one request and written together, so a run cut short by the allowance or a failure has
    /// still stored every description it paid for, with its vector.
    /// </summary>
    private async Task DescribeAsync(
        IReadOnlyList<KnowledgeHit> pictures,
        int version,
        AiCaptionsConfiguration settings,
        RunState run,
        CancellationToken cancellationToken)
    {
        var chain = await ReadChainAsync(cancellationToken).ConfigureAwait(false);
        if (chain.Count == 0)
        {
            LogNoModel(logger);
            run.NoModel = true;
            return;
        }

        var batchSize = Math.Max(1, aiConfiguration.Value.OpenRouter.EmbeddingBatchSize);

        foreach (var group in pictures.Chunk(batchSize))
        {
            // The group's one embedding request is claimed before any picture is asked about, so a
            // description paid for always gets its vector: claimed after, the last chat request of the
            // share would leave the group's descriptions stored but unfindable.
            if (!await TryReserveAsync(1, cancellationToken).ConfigureAwait(false))
            {
                LogBudgetExhausted(logger, run.Attempted);
                run.BudgetExhausted = true;
                return;
            }

            var described = new List<(KnowledgeHit Picture, string Caption)>(group.Length);

            foreach (var picture in group)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!await TryReserveAsync(1, cancellationToken).ConfigureAwait(false))
                {
                    LogBudgetExhausted(logger, run.Attempted);
                    run.BudgetExhausted = true;
                    break;
                }

                run.Attempted++;
                var outcome = await AskAsync(picture, chain, settings, cancellationToken).ConfigureAwait(false);

                switch (outcome.Kind)
                {
                    case AskOutcomeKind.Described:
                        described.Add((picture, outcome.Caption!));
                        run.Described++;
                        run.ConsecutiveMisses = 0;
                        break;

                    case AskOutcomeKind.Unusable:
                        run.Unusable++;
                        run.ConsecutiveMisses++;
                        break;

                    case AskOutcomeKind.RateLimited:
                        run.Failed++;
                        run.RateLimited = true;
                        break;

                    case AskOutcomeKind.Failed:
                        run.Failed++;
                        run.ConsecutiveMisses++;
                        // The catalog has demoted whichever model was to blame; ask the next one.
                        chain = await ReadChainAsync(cancellationToken).ConfigureAwait(false);
                        if (chain.Count == 0)
                        {
                            LogNoModel(logger);
                            run.NoModel = true;
                        }

                        break;
                }

                if (run.RateLimited || run.NoModel)
                {
                    break;
                }

                if (run.ConsecutiveMisses >= Math.Max(1, settings.MaxConsecutiveFailures))
                {
                    LogStoppedForFailures(logger, run.ConsecutiveMisses);
                    run.StoppedForFailures = true;
                    break;
                }
            }

            if (described.Count > 0)
            {
                await WriteAsync(described, version, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Nothing to embed, so the request claimed for it goes back to the day's allowance.
                await budget.ReleaseRequestsAsync(Today(), 1, cancellationToken).ConfigureAwait(false);
            }

            if (run.MustStop)
            {
                return;
            }
        }
    }

    private async Task<AskOutcome> AskAsync(
        KnowledgeHit picture,
        IReadOnlyList<string> chain,
        AiCaptionsConfiguration settings,
        CancellationToken cancellationToken)
    {
        // Inline when the relay holds a copy, so describing never depends on the provider reaching
        // this host — the same reason the embedder gets pictures inline.
        var inline = await imageRelay.ReadAsDataUrlAsync(picture.ImageUrl!, cancellationToken).ConfigureAwait(false);
        var image = string.IsNullOrEmpty(inline) ? picture.ImageUrl! : inline;

        var messages = PictureCaptionPrompt.BuildMessages(picture, image);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds)));

        Result<AiChatResponse> completion;
        try
        {
            completion = await chatClient.CompleteAsync(
                    new AiChatRequest(chain, messages, PictureCaptionPrompt.Temperature, PictureCaptionPrompt.MaxTokens),
                    timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The head model stalled on this picture; the next one is worth asking, as for an answer.
            LogTimedOut(logger, picture.ImageUrl!, chain.Count > 0 ? chain[0] : "?");
            modelCatalog.ReportFailedRequest(chain, Error.Unexpected(ChatErrorCodes.TimedOut, "The model did not describe the picture in time."));
            return AskOutcome.Failed;
        }

        if (completion.IsFailure)
        {
            LogRequestFailed(logger, picture.ImageUrl!, completion.Error.Code, completion.Error.Message);
            modelCatalog.ReportFailedRequest(chain, completion.Error);

            return completion.Error.Code == ChatErrorCodes.RateLimited ? AskOutcome.RateLimited : AskOutcome.Failed;
        }

        var response = completion.Value;
        await budget.RecordTokenUsageAsync(Today(), response.Usage.PromptTokens, response.Usage.CompletionTokens, cancellationToken)
            .ConfigureAwait(false);
        modelCatalog.ReportChainOutcome(chain, response.ModelUsed);

        if (modelCatalog.IsUnfitToAnswer(response.ModelUsed))
        {
            LogUnusable(logger, picture.ImageUrl!, response.ModelUsed, "the model is one that must not answer");
            return AskOutcome.Unusable;
        }

        var caption = PictureCaptionPrompt.Clean(response.Text, settings.MaxCaptionLength);
        if (caption is null)
        {
            LogUnusable(logger, picture.ImageUrl!, response.ModelUsed, Shorten(response.Text));
            return AskOutcome.Unusable;
        }

        LogDescribed(logger, picture.ImageUrl!, response.ModelUsed, caption);
        return AskOutcome.Described(caption);
    }

    /// <summary>
    /// Stores one group's descriptions, each with the text vector that now includes it, in the one
    /// embedding request claimed for the group. When the embedder fails, the descriptions are stored
    /// all the same — searched no better until the next re-index, but shown and read by the model from
    /// now on, and never paid for twice.
    /// </summary>
    private async Task WriteAsync(
        IReadOnlyList<(KnowledgeHit Picture, string Caption)> described,
        int version,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ReadOnlyMemory<float>>? vectors = null;

        var inputs = described
            .Select(entry => (EmbeddingInput)new TextEmbeddingInput(EmbeddingTextBuilder.Build(
                entry.Picture.Country, entry.Picture.Title, entry.Picture.SectionPath, entry.Picture.Text, entry.Caption)))
            .ToList();

        var embedded = await embedder.EmbedAsync(inputs, cancellationToken).ConfigureAwait(false);
        if (embedded.IsSuccess && embedded.Value.Count == described.Count)
        {
            vectors = embedded.Value;
        }
        else
        {
            LogEmbeddingFailed(logger, described.Count, embedded.IsFailure ? embedded.Error.Message : "the embedder returned the wrong number of vectors");
        }

        var descriptions = described
            .Select((entry, index) => new PictureDescription(
                entry.Picture.Id,
                new GeneratedCaption(entry.Caption, version),
                vectors?[index]))
            .ToList();

        await knowledgeIndex.WriteCaptionsAsync(descriptions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The models that read pictures, best first; empty when there is none today.</summary>
    private Task<IReadOnlyList<string>> ReadChainAsync(CancellationToken cancellationToken) =>
        modelCatalog.ReadChainAsync(
            new ChatModelRequirements(NeedsImageInput: true, MinContextLength: aiConfiguration.Value.OpenRouter.MinContextLength),
            cancellationToken);

    /// <summary>
    /// Claims against a share of the day's allowance, as indexing does: the shared counter past this
    /// ceiling stops describing while answering carries on up to the full allowance.
    /// </summary>
    private Task<bool> TryReserveAsync(int requests, CancellationToken cancellationToken)
    {
        var percent = Math.Clamp(captionsConfiguration.Value.MaxDailyBudgetPercent, 1, 100);
        var ceiling = Math.Max(1, aiConfiguration.Value.OpenRouter.DailyRequestBudget * percent / 100);

        return budget.TryReserveRequestsAsync(Today(), requests, ceiling, cancellationToken);
    }

    private DateOnly Today() => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    private static string Shorten(string? text)
    {
        var flat = string.Join(' ', (text ?? string.Empty).Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= 120 ? flat : $"{flat[..120]}…";
    }

    private enum AskOutcomeKind
    {
        Described,
        Unusable,
        Failed,
        RateLimited
    }

    private readonly record struct AskOutcome(AskOutcomeKind Kind, string? Caption = null)
    {
        public static AskOutcome Described(string caption) => new(AskOutcomeKind.Described, caption);
        public static readonly AskOutcome Unusable = new(AskOutcomeKind.Unusable);
        public static readonly AskOutcome Failed = new(AskOutcomeKind.Failed);
        public static readonly AskOutcome RateLimited = new(AskOutcomeKind.RateLimited);
    }

    private sealed class RunState
    {
        public int Attempted { get; set; }
        public int Described { get; set; }
        public int Unusable { get; set; }
        public int Failed { get; set; }
        public int ConsecutiveMisses { get; set; }
        public bool BudgetExhausted { get; set; }
        public bool NoModel { get; set; }
        public bool StoppedForFailures { get; set; }
        public bool RateLimited { get; set; }

        public bool MustStop => BudgetExhausted || NoModel || StoppedForFailures || RateLimited;
    }

    [LoggerMessage(LogLevel.Information,
        "Picture descriptions: {Attempted} asked, {Described} described, {Unusable} unusable, {Failed} failed; {Remaining} still waiting.")]
    static partial void LogRun(ILogger logger, int attempted, int described, int unusable, int failed, long remaining);

    [LoggerMessage(LogLevel.Debug, "Described {ImageUrl} with {Model}: {Caption}")]
    static partial void LogDescribed(ILogger logger, string imageUrl, string model, string caption);

    [LoggerMessage(LogLevel.Warning, "No usable description of {ImageUrl} from {Model}; left for a later run ({Reply}).")]
    static partial void LogUnusable(ILogger logger, string imageUrl, string model, string reply);

    [LoggerMessage(LogLevel.Warning, "Could not describe {ImageUrl} ({Code}): {Reason}")]
    static partial void LogRequestFailed(ILogger logger, string imageUrl, string code, string reason);

    [LoggerMessage(LogLevel.Warning, "{Model} did not describe {ImageUrl} in time; left for a later run.")]
    static partial void LogTimedOut(ILogger logger, string imageUrl, string model);

    [LoggerMessage(LogLevel.Warning, "No free model that reads pictures is available; nothing described.")]
    static partial void LogNoModel(ILogger logger);

    [LoggerMessage(LogLevel.Information, "Stopping after {Attempted} picture(s): describing has used its share of today's AI allowance.")]
    static partial void LogBudgetExhausted(ILogger logger, int attempted);

    [LoggerMessage(LogLevel.Warning, "Stopping: {Misses} unusable or failed replies in a row; the rest is left for a later run.")]
    static partial void LogStoppedForFailures(ILogger logger, int misses);

    [LoggerMessage(LogLevel.Warning,
        "Stored {Count} description(s) without re-embedding them ({Reason}); they are searched no better until their guide is re-indexed.")]
    static partial void LogEmbeddingFailed(ILogger logger, int count, string reason);
}
