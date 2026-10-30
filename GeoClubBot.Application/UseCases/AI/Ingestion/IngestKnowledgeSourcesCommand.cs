using System.Security.Cryptography;
using System.Text;
using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.AI.Ingestion;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.AI.Ingestion;

/// <param name="SourceType">Restricts the run to one extractor family; null processes anything due.</param>
/// <param name="Force">Ingests even when the content is unchanged, for a deliberate rebuild.</param>
public sealed record IngestKnowledgeSourcesCommand(
    int? MaxSources = null,
    string? SourceType = null,
    bool Force = false) : ICommand<Result<IngestionReport>>;

/// <param name="Failed">Sources that could not be read for a reason that may pass; retried with backoff.</param>
/// <param name="BudgetExhausted">True when the run stopped early because the daily allowance ran out.</param>
/// <param name="RateLimited">
/// True when the run stopped because the provider was still rate-limiting after its window had been
/// waited out — a sign something else is spending the same key. Carrying on would record every remaining
/// source as failed for a reason that has nothing to do with them.
/// </param>
/// <param name="Gone">Sources found deleted upstream; checked again after the recheck interval.</param>
/// <param name="NotPublic">Sources found no longer shared publicly; checked again after the recheck interval.</param>
/// <param name="TooLarge">Of the skipped, those whose every export was too large to take.</param>
/// <param name="HostRefused">
/// Sources left exactly as they were because their host refused a request earlier in the run. Not
/// attempted: a refusing host says nothing about the guides it serves. Their slots go to the next due
/// sources of other hosts.
/// </param>
/// <param name="Removed">
/// Sources whose chunks were removed from the index — still gone a recheck later, or unlisted by every
/// catalogue past the grace period — so answers stop citing links that lead nowhere.
/// </param>
public sealed record IngestionReport(
    int Attempted,
    int Ingested,
    int Unchanged,
    int Failed,
    int Skipped,
    int ChunksWritten,
    bool BudgetExhausted,
    bool RateLimited = false,
    int Gone = 0,
    int NotPublic = 0,
    int TooLarge = 0,
    int HostRefused = 0,
    int Removed = 0)
{
    /// <summary>Sources that are deleted or no longer shared, together.</summary>
    public int Unavailable => Gone + NotPublic;
}

public sealed partial class IngestKnowledgeSourcesHandler(
    IKnowledgeSourceRepository sources,
    ISourceExtractorRegistry extractors,
    IEmbedder embedder,
    IKnowledgeIndex knowledgeIndex,
    IImageRelay imageRelay,
    IAiBudgetRepository budget,
    IOptions<AiConfiguration> aiConfiguration,
    IOptions<AiIngestionConfiguration> ingestionConfiguration,
    ILogger<IngestKnowledgeSourcesHandler> logger)
    : IRequestHandler<IngestKnowledgeSourcesCommand, Result<IngestionReport>>
{
    public async Task<Result<IngestionReport>> Handle(
        IngestKnowledgeSourcesCommand request,
        CancellationToken cancellationToken)
    {
        var settings = ingestionConfiguration.Value;
        var now = DateTimeOffset.UtcNow;
        var limit = Math.Max(1, request.MaxSources ?? settings.MaxSourcesPerRun);
        var schedule = IngestionSchedule.From(settings);

        await knowledgeIndex.EnsureCollectionAsync(cancellationToken).ConfigureAwait(false);

        // Read as the run goes rather than as a fixed batch, so that the slots of sources passed over for a
        // refusing host go to the next sources of other hosts.
        var due = sources.StreamDueForIngestAsync(
            now,
            schedule,
            // At least one, or a setting of zero would leave every failed source failed for good.
            Math.Max(1, settings.MaxFailingRetriesPerRun),
            request.SourceType,
            request.Force,
            cancellationToken);

        var report = new Counters();

        // Hosts that refused a request this run. One open circuit used to blame every source after it,
        // guide sites the breaker had nothing against included, and back each of them off.
        var refusedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Sources handed to an extractor, a refused one included: the limit bounds the requests a run
        // makes of the guide hosts, and a source passed over for its host makes none.
        var handedOver = 0;

        await foreach (var source in due.ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var host = HostOf(source);
            if (refusedHosts.Contains(host))
            {
                // Left exactly as it was, so it keeps its place at the front of the next run's queue.
                report.Apply(Outcome.HostRefused);
                continue;
            }

            handedOver++;
            var outcome = await IngestOneAsync(source, request.Force, settings, schedule, now, cancellationToken)
                .ConfigureAwait(false);

            report.Apply(outcome);

            if (outcome.Kind == OutcomeKind.HostRefused)
            {
                refusedHosts.Add(host);
            }
            else if (outcome.Kind == OutcomeKind.OutOfBudget)
            {
                // Stop the whole run: every remaining source needs embedding requests we no longer
                // have, and attempting them would only earn a stream of rate-limit errors.
                LogBudgetExhausted(logger, report.Attempted);
                break;
            }
            else if (outcome.RateLimited)
            {
                // The request layer has already waited out the provider's window once. Still being
                // limited means the window is being spent elsewhere, and every further source would hit
                // the same wall.
                LogRateLimited(logger, report.Attempted);
                break;
            }

            if (handedOver >= limit)
            {
                break;
            }
        }

        report.AddRemoved(await RemoveUnlistedSourcesAsync(request.SourceType, settings, now, cancellationToken)
            .ConfigureAwait(false));

        return report.ToReport();
    }

    /// <summary>
    /// The host a source is fetched from. The extractors fetch from the host of the source's own link,
    /// so it is also what a refusal is about; a link that will not parse groups with its own family.
    /// </summary>
    private static string HostOf(KnowledgeSource source) =>
        Uri.TryCreate(source.Url, UriKind.Absolute, out var url) ? url.Host : source.SourceType;

    private async Task<Outcome> IngestOneAsync(
        KnowledgeSource source,
        bool force,
        AiIngestionConfiguration settings,
        KnowledgeSourceSchedule schedule,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var extractor = extractors.ResolveByType(source.SourceType);
        if (extractor is null)
        {
            source.MarkSkipped($"No extractor is registered for '{source.SourceType}'.", now);
            LogSourceSkipped(logger, source.SourceType, source.NaturalKey, NoExtractorCode, source.StatusReason!);
            return Outcome.Skipped(tooLarge: false);
        }

        var descriptor = new SourceDescriptor(
            source.SourceType, source.NaturalKey, new Uri(source.Url),
            source.Title, source.Country, source.Continent, source.Author, source.Priority);

        var extracted = await extractor.ExtractAsync(descriptor, cancellationToken).ConfigureAwait(false);
        if (extracted.IsFailure)
        {
            return await RecordExtractionFailureAsync(source, extracted.Error, schedule, now, cancellationToken)
                .ConfigureAwait(false);
        }

        var document = extracted.Value;
        if (document.Chunks.Count == 0)
        {
            source.MarkSkipped("The source contained no indexable content.", now);
            LogSourceSkipped(logger, source.SourceType, source.NaturalKey, NoContentCode, source.StatusReason!);
            return Outcome.Skipped(tooLarge: false);
        }

        if (document.Degraded && source.HoldsIndexedPictures)
        {
            // Text read in place of an export that failed this time. Writing it would sweep every
            // picture indexed from the full export, only for them to be embedded again — at a request
            // per batch of the same daily allowance — once it answers. What is indexed stays, pictures
            // still owed their embedding included: writing over them would also clear the debt.
            source.MarkFailed(
                "The full export failed and only the text could be read; kept the version already indexed with its pictures.",
                now);
            LogSourceFailed(logger, source.SourceType, source.NaturalKey, DegradedCode, source.StatusReason!,
                source.ConsecutiveFailures);
            return Outcome.Failed;
        }

        var chunks = ContentChunker.Chunk(document.Chunks);
        var contentHash = ComputeHash(chunks);

        if (!force
            && string.Equals(source.ContentHash, contentHash, StringComparison.Ordinal)
            && !source.NeedsImageBackfill)
        {
            source.MarkUnchanged(now);
            return Outcome.Unchanged;
        }

        var ingestRun = Guid.NewGuid().ToString("N");
        var written = await WriteAsync(source, descriptor, chunks, ingestRun, settings, cancellationToken)
            .ConfigureAwait(false);

        if (written.IsFailure)
        {
            // Neither is the source's fault, so its state is left untouched and it is first in line next
            // run. Recording a rate limit as a failure is what backed off good sources one after another,
            // as each remaining source in the run met the same spent window.
            if (written.Error.Code == BudgetExhaustedCode)
            {
                return Outcome.OutOfBudget;
            }

            if (written.Error.Code == EmbeddingErrorCodes.RateLimited)
            {
                return Outcome.RateLimitedBeforeWriting;
            }

            source.MarkFailed(written.Error.Message, now);
            LogSourceFailed(logger, source.SourceType, source.NaturalKey, written.Error.Code, written.Error.Message,
                source.ConsecutiveFailures);
            return Outcome.Failed;
        }

        // Sweep only after a successful write, so the index is never briefly missing this source.
        await knowledgeIndex.SweepAsync(source.SourceType, source.NaturalKey, ingestRun, cancellationToken)
            .ConfigureAwait(false);

        source.MarkIngested(contentHash, document.SourceUpdatedAtUtc, written.Value.ChunkCount,
            written.Value.ImageCount, now, written.Value.ImagesDeferred);
        LogIngested(logger, source.SourceType, source.NaturalKey, written.Value.ChunkCount, written.Value.ImageCount);

        return Outcome.Ingested(written.Value.ChunkCount, written.Value.RateLimited);
    }

    /// <summary>
    /// Records why a source could not be read, by what can be done about it.
    ///
    /// Each verdict is logged on its own: a failure used to show up only as a count in the run's summary
    /// and a redacted HTTP line, which is how ten broken guides went unnoticed night after night.
    /// </summary>
    private async Task<Outcome> RecordExtractionFailureAsync(
        KnowledgeSource source,
        Error error,
        KnowledgeSourceSchedule schedule,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // About the host, not this source: it is left as it was, like a provider rate limit.
        if (error.Code == SourceExtractionErrorCodes.HostRefused)
        {
            LogHostRefused(logger, HostOf(source), source.SourceType, source.NaturalKey, error.Message);
            return Outcome.HostRefused;
        }

        switch (error.Type)
        {
            // Deleted, or no longer shared: only the owner can change that, so it is neither retried with
            // backoff nor given up on, but checked again on its own, much longer interval.
            case ErrorType.NotFound or ErrorType.Forbidden:
                {
                    var gone = error.Type == ErrorType.NotFound;
                    source.MarkUnavailable(error.Message, now);

                    var recheckDays = (int)Math.Round(schedule.UnavailableRecheckInterval.TotalDays);
                    LogSourceUnavailable(logger, source.SourceType, source.NaturalKey, error.Code, error.Message,
                        source.ConsecutiveFailures, recheckDays);

                    // Still gone a whole recheck later, so a citation of it now leads nowhere. Only a deletion
                    // counts: a private document may be shared again, and its link still names its owner.
                    var removed = gone && source.IsConfirmedUnavailable && source.HoldsIndexedContent;
                    if (removed)
                    {
                        var points = await RemoveFromIndexAsync(source, cancellationToken).ConfigureAwait(false);
                        LogRemovedGone(logger, source.SourceType, source.NaturalKey, points, source.ConsecutiveFailures);
                    }

                    return Outcome.Unavailable(gone, removed);
                }

            // Understood and simply not ingestible — a listing page, an empty album, an export too
            // large in every form. Retrying it nightly would waste the whole run's budget.
            case ErrorType.Validation:
                source.MarkSkipped(error.Message, now);
                LogSourceSkipped(logger, source.SourceType, source.NaturalKey, error.Code, error.Message);
                return Outcome.Skipped(tooLarge: error.Code == SourceExtractionErrorCodes.ExportTooLarge);

            default:
                source.MarkFailed(error.Message, now);
                LogSourceFailed(logger, source.SourceType, source.NaturalKey, error.Code, error.Message,
                    source.ConsecutiveFailures);
                return Outcome.Failed;
        }
    }

    /// <summary>
    /// Removes the chunks of sources no catalogue has listed for the grace period.
    ///
    /// A sync tombstones rather than deletes, so a guide dropped from a library for a week keeps its
    /// chunks. Without this, though, it kept them for ever — answers went on citing guides the library
    /// had withdrawn, and only a successful re-index ever swept anything.
    /// </summary>
    /// <returns>How many sources had their chunks removed.</returns>
    private async Task<int> RemoveUnlistedSourcesAsync(
        string? sourceType,
        AiIngestionConfiguration settings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (settings.RemovedSourceGraceDays <= 0)
        {
            return 0;
        }

        var unlisted = await sources.ReadRemovedWithIndexedContentAsync(
            now - TimeSpan.FromDays(settings.RemovedSourceGraceDays),
            sourceType,
            cancellationToken).ConfigureAwait(false);

        foreach (var source in unlisted)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var points = await RemoveFromIndexAsync(source, cancellationToken).ConfigureAwait(false);
            LogRemovedUnlisted(logger, source.SourceType, source.NaturalKey, points, settings.RemovedSourceGraceDays);
        }

        return unlisted.Count;
    }

    /// <summary>Removes every chunk of a source from the index, and records that it holds none.</summary>
    private async Task<int> RemoveFromIndexAsync(KnowledgeSource source, CancellationToken cancellationToken)
    {
        // A run id nothing was ever written under, so the sweep finds every point of the source stale.
        var removed = await knowledgeIndex
            .SweepAsync(source.SourceType, source.NaturalKey, Guid.NewGuid().ToString("N"), cancellationToken)
            .ConfigureAwait(false);

        source.MarkIndexedContentRemoved();
        return removed;
    }

    private async Task<Result<WriteResult>> WriteAsync(
        KnowledgeSource source,
        SourceDescriptor descriptor,
        IReadOnlyList<ExtractedChunk> chunks,
        string ingestRun,
        AiIngestionConfiguration settings,
        CancellationToken cancellationToken)
    {
        // Dropped before the relay is asked to fetch them, let alone the provider to read them.
        chunks = DropUnusableImages(chunks);

        // Rewritten before anything else uses the URL, so the embedder and the eventual Discord embed
        // both point at the same fetchable image.
        chunks = await RelayImagesAsync(chunks, cancellationToken).ConfigureAwait(false);

        var textInputs = chunks
            .Select(chunk => (EmbeddingInput)new TextEmbeddingInput(EmbeddingTextBuilder.Build(descriptor, chunk)))
            .ToList();

        if (!await TryReserveForInputsAsync(textInputs.Count, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict(BudgetExhaustedCode, "Indexing has used its share of today's AI allowance.");
        }

        var textVectors = await embedder.EmbedAsync(textInputs, cancellationToken).ConfigureAwait(false);
        if (textVectors.IsFailure)
        {
            return textVectors.Error;
        }

        var images = await EmbedImagesAsync(chunks, settings, cancellationToken).ConfigureAwait(false);

        var points = new List<KnowledgePoint>(chunks.Count);
        for (var index = 0; index < chunks.Count; index++)
        {
            var chunk = chunks[index];

            // Written out rather than folded into the constructor call: an image chunk whose image
            // could not be embedded must carry no image vector at all, and that distinction is too
            // easy to lose in a conditional expression.
            ReadOnlyMemory<float>? imageVector = null;
            if (chunk.ImageUrl is { } imageUrl && images.Vectors.TryGetValue(imageUrl, out var embedded))
            {
                imageVector = embedded;
            }

            points.Add(new KnowledgePoint(
                new KnowledgeChunk
                {
                    SourceType = descriptor.SourceType,
                    SourceKey = descriptor.NaturalKey,
                    LocalKey = chunk.LocalKey,
                    Kind = chunk.ImageUrl is null ? KnowledgeChunkKind.Text : KnowledgeChunkKind.Image,
                    Text = chunk.Text,
                    SourceUrl = BuildSourceUrl(descriptor, chunk),
                    ImageUrl = chunk.ImageUrl,
                    Title = descriptor.Title ?? source.Title,
                    Country = descriptor.Country,
                    SectionPath = chunk.SectionPath,
                    Author = descriptor.Author,
                    Priority = descriptor.Priority
                },
                textVectors.Value[index],
                imageVector));
        }

        await knowledgeIndex.UpsertAsync(points, ingestRun, cancellationToken).ConfigureAwait(false);

        return new WriteResult(points.Count, images.Vectors.Count, images.Deferred, images.RateLimited);
    }

    /// <summary>
    /// Keeps the text of chunks whose picture is in a format nothing downstream can read.
    ///
    /// The chunk stays indexed and searchable; only the image URL goes. Storing it would mean a
    /// citation pointing at something Discord cannot render, and sending it would fail the whole
    /// embedding request it travelled in — which is what a guide's vector illustrations did every
    /// night, costing the images batched alongside them and the requests spent finding the culprit.
    /// </summary>
    private IReadOnlyList<ExtractedChunk> DropUnusableImages(IReadOnlyList<ExtractedChunk> chunks)
    {
        static bool IsUnusable(ExtractedChunk chunk) =>
            chunk.ImageUrl is not null && !ImageFormats.CanEmbed(chunk.ImageUrl);

        var dropped = chunks.Count(IsUnusable);
        if (dropped == 0)
        {
            return chunks;
        }

        LogImagesUnusable(logger, dropped);

        return [.. chunks.Select(chunk => IsUnusable(chunk) ? chunk with { ImageUrl = null } : chunk)];
    }

    /// <summary>
    /// Replaces image URLs the AI provider cannot fetch with copies served from our own host.
    ///
    /// Resolved once per distinct URL, because a guide often illustrates several tips with the same
    /// picture and each copy would otherwise be downloaded again.
    /// </summary>
    private async Task<IReadOnlyList<ExtractedChunk>> RelayImagesAsync(
        IReadOnlyList<ExtractedChunk> chunks,
        CancellationToken cancellationToken)
    {
        if (!imageRelay.IsEnabled)
        {
            return chunks;
        }

        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var imageUrl in chunks.Select(chunk => chunk.ImageUrl).OfType<string>().Distinct(StringComparer.Ordinal))
        {
            resolved[imageUrl] = await imageRelay.ResolveAsync(imageUrl, cancellationToken).ConfigureAwait(false);
        }

        return [.. chunks.Select(chunk => chunk.ImageUrl is { } url && resolved.TryGetValue(url, out var replacement)
            ? chunk with { ImageUrl = replacement }
            : chunk)];
    }

    /// <summary>
    /// Embeds a source's images in request-sized groups, keeping whatever succeeds.
    ///
    /// Images are embedded apart from text so that losing them costs image search, never the source.
    /// Each group stands alone, so one that fails costs its own images rather than every image after it.
    /// Relayed images travel inline, so the provider never has to fetch them back from this host.
    /// </summary>
    private async Task<ImageEmbeddingState> EmbedImagesAsync(
        IReadOnlyList<ExtractedChunk> chunks,
        AiIngestionConfiguration settings,
        CancellationToken cancellationToken)
    {
        var state = new ImageEmbeddingState();
        if (!settings.EmbedImages)
        {
            return state;
        }

        var openRouter = aiConfiguration.Value.OpenRouter;
        var maxInputs = Math.Max(1, openRouter.EmbeddingBatchSize);
        var maxBytes = Math.Max(1, openRouter.EmbeddingMaxRequestBytes);

        var group = new List<ImageInput>();
        var groupBytes = 0L;

        foreach (var imageUrl in chunks.Select(chunk => chunk.ImageUrl).OfType<string>().Distinct(StringComparer.Ordinal))
        {
            var image = await PrepareImageAsync(imageUrl, cancellationToken).ConfigureAwait(false);

            // Bounded by bytes as well as count: inline pictures make a batch of thirty-two anywhere from
            // a few kilobytes to far more than the provider accepts in one request.
            if (group.Count > 0 && (group.Count == maxInputs || groupBytes + image.Size > maxBytes))
            {
                await EmbedImageGroupAsync(group, state, cancellationToken).ConfigureAwait(false);
                group = [];
                groupBytes = 0;

                if (state.MustStop)
                {
                    return state;
                }
            }

            group.Add(image);
            groupBytes += image.Size;
        }

        if (group.Count > 0)
        {
            await EmbedImageGroupAsync(group, state, cancellationToken).ConfigureAwait(false);
        }

        return state;
    }

    /// <summary>The image as the provider will receive it: inline when the relay holds a copy, by URL otherwise.</summary>
    private async Task<ImageInput> PrepareImageAsync(string imageUrl, CancellationToken cancellationToken)
    {
        var inline = await imageRelay.ReadAsDataUrlAsync(imageUrl, cancellationToken).ConfigureAwait(false);
        var sent = string.IsNullOrEmpty(inline) ? imageUrl : inline;

        return new ImageInput(imageUrl, new ImageEmbeddingInput(sent), sent.Length);
    }

    /// <summary>
    /// Embeds one group in one request, and decides what a failure is worth.
    ///
    /// A group the provider rejects is split in half until the image at fault is found: one dead picture
    /// would otherwise take its batch-mates down with it, and on every run, because a rejection recurs.
    /// Past a handful of rejections in one source it stops narrowing, since that reads as the provider
    /// refusing images in general, and narrowing that down one request at a time would spend the day's
    /// allowance learning nothing.
    /// </summary>
    private async Task EmbedImageGroupAsync(
        IReadOnlyList<ImageInput> group,
        ImageEmbeddingState state,
        CancellationToken cancellationToken)
    {
        if (state.MustStop)
        {
            return;
        }

        if (state.RejectingImagesInGeneral)
        {
            state.Defer();
            return;
        }

        if (!await TryReserveRequestsAsync(1, cancellationToken).ConfigureAwait(false))
        {
            LogImagesDeferred(logger, group.Count);
            state.StopForBudget();
            return;
        }

        var embedded = await embedder.EmbedAsync([.. group.Select(image => image.Input)], cancellationToken)
            .ConfigureAwait(false);

        if (embedded.IsSuccess)
        {
            for (var index = 0; index < group.Count; index++)
            {
                state.Vectors[group[index].Url] = embedded.Value[index];
            }

            return;
        }

        var error = embedded.Error;

        if (error.Code == EmbeddingErrorCodes.RateLimited)
        {
            LogImagesRateLimited(logger, group.Count);
            state.StopForRateLimit();
            return;
        }

        if (error.Code != EmbeddingErrorCodes.Rejected)
        {
            // About the moment, not the images: they go back in the queue for the next run.
            LogImageGroupFailed(logger, group.Count, error.Message);
            state.Defer();
            return;
        }

        if (group.Count > 1)
        {
            // The provider names the URL it refused, so usually no narrowing is needed at all: drop
            // what it named and re-send the rest in one request. Halving costs a request per level,
            // and every one of them comes out of the same daily allowance the guides are indexed with.
            var named = ReadNamedImages(group, error.Message);
            if (named.Count > 0)
            {
                foreach (var image in named)
                {
                    RejectImage(image, error, state);
                }

                // Re-entered rather than continued inline, so a rejection that has just been judged
                // general stops here on the guard at the top instead of spending another request.
                await EmbedImageGroupAsync([.. group.Except(named)], state, cancellationToken).ConfigureAwait(false);
                return;
            }

            var half = group.Count / 2;
            await EmbedImageGroupAsync([.. group.Take(half)], state, cancellationToken).ConfigureAwait(false);
            await EmbedImageGroupAsync([.. group.Skip(half)], state, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Found it, by narrowing or by name.
        RejectImage(group[0], error, state);
    }

    /// <summary>
    /// The images of this group the provider's own message names, and never all of them — a message
    /// naming every image says the request was refused rather than a picture in it, and dropping the
    /// lot on that reading would silently cost a source all its images.
    ///
    /// Matched without the query string: the message is truncated to keep a failure reason readable,
    /// and a cache-busting <c>?v=…</c> is the first thing to be cut off.
    /// </summary>
    private static IReadOnlyList<ImageInput> ReadNamedImages(IReadOnlyList<ImageInput> group, string message)
    {
        var named = group
            .Where(image => message.Contains(UrlWithoutQuery(image.Url), StringComparison.Ordinal))
            .ToList();

        return named.Count == group.Count ? [] : named;
    }

    private static string UrlWithoutQuery(string url)
    {
        var query = url.IndexOf('?', StringComparison.Ordinal);
        return query < 0 ? url : url[..query];
    }

    /// <summary>
    /// Records one image as refused for good — indexed without its picture, and not retried, because
    /// it would be refused the same way. Past a handful in one source it stops counting them
    /// individually: that reads as the provider refusing images in general, and narrowing that down
    /// one request at a time would spend the day's allowance learning nothing.
    /// </summary>
    private void RejectImage(ImageInput image, Error error, ImageEmbeddingState state)
    {
        if (++state.IsolatedRejections > MaxIsolatedImageRejections)
        {
            LogImagesRejectedInGeneral(logger, MaxIsolatedImageRejections, error.Message);
            state.RejectingImagesInGeneral = true;
            state.Defer();
            return;
        }

        LogImageRejected(logger, image.Url, error.Message);
    }

    /// <summary>Claims what embedding these inputs will cost, before spending it.</summary>
    private Task<bool> TryReserveForInputsAsync(int inputCount, CancellationToken cancellationToken)
    {
        var batchSize = Math.Max(1, aiConfiguration.Value.OpenRouter.EmbeddingBatchSize);
        return TryReserveRequestsAsync((int)Math.Ceiling(inputCount / (double)batchSize), cancellationToken);
    }

    private async Task<bool> TryReserveRequestsAsync(int requests, CancellationToken cancellationToken) =>
        await budget.TryReserveRequestsAsync(
            DateOnly.FromDateTime(DateTime.UtcNow),
            requests,
            ReadIngestionDailyCap(),
            cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Indexing claims against a fraction of the day's allowance rather than all of it.
    ///
    /// Both indexing and answering increment the same daily counter, so passing a lower ceiling here
    /// is what reserves the remainder for questions: once the counter passes this share, indexing
    /// stops while answering carries on up to the full allowance. It also means indexing yields when
    /// people have already been asking questions, rather than the other way round.
    /// </summary>
    private int ReadIngestionDailyCap()
    {
        var percent = Math.Clamp(ingestionConfiguration.Value.MaxDailyBudgetPercent, 1, 100);

        // At least one request, or a small allowance combined with a small share would stall
        // indexing entirely rather than merely slowing it.
        return Math.Max(1, aiConfiguration.Value.OpenRouter.DailyRequestBudget * percent / 100);
    }

    private static string BuildSourceUrl(SourceDescriptor descriptor, ExtractedChunk chunk) =>
        string.IsNullOrWhiteSpace(chunk.Anchor)
            ? descriptor.Url.ToString()
            : $"{descriptor.Url}#{chunk.Anchor}";

    /// <summary>
    /// Hash of the chunked content, so an unchanged source costs nothing to re-check. Fields are
    /// joined with a unit separator so content containing the delimiter cannot forge a match.
    /// </summary>
    private static string ComputeHash(IReadOnlyList<ExtractedChunk> chunks)
    {
        var builder = new StringBuilder();

        // The recipe used to build the embedding text is part of what is stored, so changing it must
        // invalidate the hash. Otherwise an improvement to the header would never reach anything
        // already indexed: the chunk text is unchanged, so every source would be judged unchanged.
        builder.Append(EmbeddingTextBuilder.RecipeVersion).Append(FieldSeparator);

        foreach (var chunk in chunks)
        {
            builder.Append(chunk.LocalKey).Append(FieldSeparator)
                .Append(chunk.Text).Append(FieldSeparator)
                .Append(chunk.ImageUrl).Append(FieldSeparator);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private const char FieldSeparator = '\u001F';

    private const string BudgetExhaustedCode = "ai.budget_exhausted";

    /// <summary>
    /// Rejected images tolerated in one source before rejection is taken to mean the provider is refusing
    /// images in general rather than a few broken ones.
    /// </summary>
    private const int MaxIsolatedImageRejections = 3;

    private sealed record WriteResult(int ChunkCount, int ImageCount, bool ImagesDeferred, bool RateLimited);

    /// <param name="Url">The image as stored in the index — never the inline form, which is only for sending.</param>
    /// <param name="Size">Characters the input adds to a request body, which for an inline image is its base64.</param>
    private sealed record ImageInput(string Url, EmbeddingInput Input, long Size);

    private sealed class ImageEmbeddingState
    {
        public Dictionary<string, ReadOnlyMemory<float>> Vectors { get; } = new(StringComparer.Ordinal);

        /// <summary>Some images are still owed for a reason that may not recur, so the source comes back for them.</summary>
        public bool Deferred { get; private set; }

        public bool RateLimited { get; private set; }

        /// <summary>No further request is worth making for this source this run.</summary>
        public bool MustStop { get; private set; }

        public int IsolatedRejections { get; set; }

        public bool RejectingImagesInGeneral { get; set; }

        public void Defer() => Deferred = true;

        public void StopForBudget()
        {
            Deferred = true;
            MustStop = true;
        }

        public void StopForRateLimit()
        {
            Deferred = true;
            RateLimited = true;
            MustStop = true;
        }
    }

    /// <summary>Codes for the run's own verdicts, so every logged verdict carries one like the extractors' do.</summary>
    private const string NoExtractorCode = "ai.no_extractor";

    private const string NoContentCode = "ai.no_indexable_content";

    private const string DegradedCode = "ai.export_degraded";

    private enum OutcomeKind
    {
        Ingested,
        Unchanged,
        Failed,
        Skipped,
        Unavailable,
        OutOfBudget,

        /// <summary>Refused by the provider before anything was written; the source is left as it was.</summary>
        RateLimitedBeforeWriting,

        /// <summary>Its host refused the request; the source is left as it was.</summary>
        HostRefused
    }

    /// <param name="Gone">For an unavailable source, whether it was deleted rather than unshared.</param>
    /// <param name="TooLarge">For a skipped source, whether every export of it was too large.</param>
    /// <param name="Removed">Whether the source's chunks were removed from the index.</param>
    private readonly record struct Outcome(
        OutcomeKind Kind,
        int ChunkCount = 0,
        bool RateLimited = false,
        bool Gone = false,
        bool TooLarge = false,
        bool Removed = false)
    {
        public static Outcome Ingested(int chunkCount, bool rateLimited) =>
            new(OutcomeKind.Ingested, chunkCount, rateLimited);

        public static Outcome Skipped(bool tooLarge) => new(OutcomeKind.Skipped, TooLarge: tooLarge);

        public static Outcome Unavailable(bool gone, bool removed) =>
            new(OutcomeKind.Unavailable, Gone: gone, Removed: removed);

        public static readonly Outcome Unchanged = new(OutcomeKind.Unchanged);
        public static readonly Outcome Failed = new(OutcomeKind.Failed);
        public static readonly Outcome OutOfBudget = new(OutcomeKind.OutOfBudget);
        public static readonly Outcome RateLimitedBeforeWriting = new(OutcomeKind.RateLimitedBeforeWriting, RateLimited: true);
        public static readonly Outcome HostRefused = new(OutcomeKind.HostRefused);
    }

    private sealed class Counters
    {
        public int Attempted { get; private set; }

        private int Ingested { get; set; }
        private int Unchanged { get; set; }
        private int Failed { get; set; }
        private int Skipped { get; set; }
        private int TooLarge { get; set; }
        private int Gone { get; set; }
        private int NotPublic { get; set; }
        private int HostRefused { get; set; }
        private int Removed { get; set; }
        private int Chunks { get; set; }
        private bool BudgetExhausted { get; set; }
        private bool RateLimited { get; set; }

        public void Apply(Outcome outcome)
        {
            RateLimited = RateLimited || outcome.RateLimited;

            switch (outcome.Kind)
            {
                case OutcomeKind.OutOfBudget:
                    BudgetExhausted = true;
                    return;

                // Neither was really attempted: the provider or the host refused before anything happened.
                case OutcomeKind.RateLimitedBeforeWriting:
                    return;

                case OutcomeKind.HostRefused:
                    HostRefused++;
                    return;
            }

            Attempted++;

            if (outcome.Removed)
            {
                Removed++;
            }

            switch (outcome.Kind)
            {
                case OutcomeKind.Ingested:
                    Ingested++;
                    Chunks += outcome.ChunkCount;
                    break;
                case OutcomeKind.Unchanged:
                    Unchanged++;
                    break;
                case OutcomeKind.Failed:
                    Failed++;
                    break;
                case OutcomeKind.Skipped:
                    Skipped++;
                    TooLarge += outcome.TooLarge ? 1 : 0;
                    break;
                case OutcomeKind.Unavailable when outcome.Gone:
                    Gone++;
                    break;
                case OutcomeKind.Unavailable:
                    NotPublic++;
                    break;
            }
        }

        /// <summary>Sources removed from the index outside any attempt, because no catalogue lists them.</summary>
        public void AddRemoved(int count) => Removed += count;

        public IngestionReport ToReport() =>
            new(Attempted, Ingested, Unchanged, Failed, Skipped, Chunks, BudgetExhausted, RateLimited,
                Gone, NotPublic, TooLarge, HostRefused, Removed);
    }

    [LoggerMessage(LogLevel.Information, "Indexed {SourceType}:{NaturalKey} - {ChunkCount} chunk(s), {ImageCount} image(s).")]
    static partial void LogIngested(ILogger logger, string sourceType, string naturalKey, int chunkCount, int imageCount);

    [LoggerMessage(LogLevel.Warning,
        "Could not index {SourceType}:{NaturalKey} ({Code}): {Reason} Failure {Failures} in a row; retried with backoff.")]
    static partial void LogSourceFailed(
        ILogger logger, string sourceType, string naturalKey, string code, string reason, int failures);

    [LoggerMessage(LogLevel.Warning,
        "Skipped {SourceType}:{NaturalKey} ({Code}): {Reason} Not retried unless an admin asks with /ai source-retry.")]
    static partial void LogSourceSkipped(ILogger logger, string sourceType, string naturalKey, string code, string reason);

    [LoggerMessage(LogLevel.Warning,
        "{SourceType}:{NaturalKey} is unavailable ({Code}): {Reason} Check {Checks} in a row; checked again in {RecheckDays} day(s).")]
    static partial void LogSourceUnavailable(
        ILogger logger, string sourceType, string naturalKey, string code, string reason, int checks, int recheckDays);

    [LoggerMessage(LogLevel.Warning,
        "Removed {Points} point(s) of {SourceType}:{NaturalKey} from the index: it was still gone after {Checks} checks.")]
    static partial void LogRemovedGone(ILogger logger, string sourceType, string naturalKey, int points, int checks);

    [LoggerMessage(LogLevel.Warning,
        "Removed {Points} point(s) of {SourceType}:{NaturalKey} from the index: no catalogue has listed it for {GraceDays} day(s).")]
    static partial void LogRemovedUnlisted(ILogger logger, string sourceType, string naturalKey, int points, int graceDays);

    [LoggerMessage(LogLevel.Warning,
        "{Host} refused the request for {SourceType}:{NaturalKey}; leaving its sources for a later run ({Reason}).")]
    static partial void LogHostRefused(ILogger logger, string host, string sourceType, string naturalKey, string reason);

    [LoggerMessage(LogLevel.Information,
        "Indexed {ImageCount} chunk(s) without their picture: the format is one the AI provider cannot read.")]
    static partial void LogImagesUnusable(ILogger logger, int imageCount);

    [LoggerMessage(LogLevel.Warning, "Could not embed {ImageCount} image(s) this run; they will be retried ({Reason}).")]
    static partial void LogImageGroupFailed(ILogger logger, int imageCount, string reason);

    [LoggerMessage(LogLevel.Warning, "Indexing without an image the embedding provider rejected: {ImageUrl} ({Reason})")]
    static partial void LogImageRejected(ILogger logger, string imageUrl, string reason);

    [LoggerMessage(LogLevel.Warning,
        "The embedding provider rejected more than {Limit} image(s) in one source; postponing the rest to a later run ({Reason}).")]
    static partial void LogImagesRejectedInGeneral(ILogger logger, int limit, string reason);

    [LoggerMessage(LogLevel.Warning,
        "Still rate-limited after waiting out the provider's window; postponed {ImageCount} image(s) to a later run.")]
    static partial void LogImagesRateLimited(ILogger logger, int imageCount);

    [LoggerMessage(LogLevel.Information, "Postponed {ImageCount} image(s) until the allowance resets.")]
    static partial void LogImagesDeferred(ILogger logger, int imageCount);

    [LoggerMessage(LogLevel.Warning,
        "Stopping ingestion after {Attempted} source(s): the AI provider is still rate-limiting after its window was waited out.")]
    static partial void LogRateLimited(ILogger logger, int attempted);

    [LoggerMessage(LogLevel.Information,
        "Stopping ingestion after {Attempted} source(s): indexing has used its share of today's AI allowance.")]
    static partial void LogBudgetExhausted(ILogger logger, int attempted);
}
