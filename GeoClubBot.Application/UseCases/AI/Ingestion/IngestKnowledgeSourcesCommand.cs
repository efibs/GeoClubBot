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

/// <param name="BudgetExhausted">True when the run stopped early because the daily allowance ran out.</param>
/// <param name="RateLimited">
/// True when the run stopped because the provider was still rate-limiting after its window had been
/// waited out — a sign something else is spending the same key. Carrying on would record every remaining
/// source as failed for a reason that has nothing to do with them.
/// </param>
public sealed record IngestionReport(
    int Attempted,
    int Ingested,
    int Unchanged,
    int Failed,
    int Skipped,
    int ChunksWritten,
    bool BudgetExhausted,
    bool RateLimited = false);

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

        await knowledgeIndex.EnsureCollectionAsync(cancellationToken).ConfigureAwait(false);

        var due = await sources.ReadDueForIngestAsync(
            now,
            TimeSpan.FromDays(Math.Max(1, settings.ReingestAfterDays)),
            limit,
            request.Force,
            cancellationToken).ConfigureAwait(false);

        if (request.SourceType is { } sourceType)
        {
            due = [.. due.Where(source => source.SourceType.Equals(sourceType, StringComparison.OrdinalIgnoreCase))];
        }

        var report = new Counters();

        foreach (var source in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outcome = await IngestOneAsync(source, request.Force, settings, now, cancellationToken)
                .ConfigureAwait(false);

            report.Apply(outcome);

            if (outcome.BudgetExhausted)
            {
                // Stop the whole run: every remaining source needs embedding requests we no longer
                // have, and attempting them would only earn a stream of rate-limit errors.
                LogBudgetExhausted(logger, report.Attempted);
                break;
            }

            if (outcome.RateLimited)
            {
                // The request layer has already waited out the provider's window once. Still being
                // limited means the window is being spent elsewhere, and every further source would hit
                // the same wall.
                LogRateLimited(logger, report.Attempted);
                break;
            }
        }

        return report.ToReport();
    }

    private async Task<Outcome> IngestOneAsync(
        KnowledgeSource source,
        bool force,
        AiIngestionConfiguration settings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var extractor = extractors.ResolveByType(source.SourceType);
        if (extractor is null)
        {
            source.MarkSkipped($"No extractor is registered for '{source.SourceType}'.", now);
            return Outcome.Skipped;
        }

        var descriptor = new SourceDescriptor(
            source.SourceType, source.NaturalKey, new Uri(source.Url),
            source.Title, source.Country, source.Continent, source.Author, source.Priority);

        var extracted = await extractor.ExtractAsync(descriptor, cancellationToken).ConfigureAwait(false);
        if (extracted.IsFailure)
        {
            // A validation failure means the source is understood and simply not ingestible — a
            // listing page, a video. Retrying it nightly would waste the whole run's budget.
            if (extracted.Error.Type == ErrorType.Validation)
            {
                source.MarkSkipped(extracted.Error.Message, now);
                return Outcome.Skipped;
            }

            source.MarkFailed(extracted.Error.Message, now);
            return Outcome.Failed;
        }

        var document = extracted.Value;
        if (document.Chunks.Count == 0)
        {
            source.MarkSkipped("The source contained no indexable content.", now);
            return Outcome.Skipped;
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

    private readonly record struct Outcome(
        bool WasIngested,
        bool WasUnchanged,
        bool WasFailed,
        bool WasSkipped,
        bool BudgetExhausted,
        bool RateLimited,
        int ChunkCount)
    {
        public static Outcome Ingested(int chunkCount, bool rateLimited) =>
            new(true, false, false, false, false, rateLimited, chunkCount);

        public static readonly Outcome Unchanged = new(false, true, false, false, false, false, 0);
        public static readonly Outcome Failed = new(false, false, true, false, false, false, 0);
        public static readonly Outcome Skipped = new(false, false, false, true, false, false, 0);
        public static readonly Outcome OutOfBudget = new(false, false, false, false, true, false, 0);

        /// <summary>Refused by the provider before anything was written; the source is left as it was.</summary>
        public static readonly Outcome RateLimitedBeforeWriting = new(false, false, false, false, false, true, 0);
    }

    private sealed class Counters
    {
        public int Attempted { get; private set; }

        private int Ingested { get; set; }
        private int Unchanged { get; set; }
        private int Failed { get; set; }
        private int Skipped { get; set; }
        private int Chunks { get; set; }
        private bool BudgetExhausted { get; set; }
        private bool RateLimited { get; set; }

        public void Apply(Outcome outcome)
        {
            RateLimited = RateLimited || outcome.RateLimited;

            if (outcome.BudgetExhausted)
            {
                BudgetExhausted = true;
                return;
            }

            // A source the provider refused before anything was written was not really attempted.
            if (outcome is { RateLimited: true, WasIngested: false })
            {
                return;
            }

            Attempted++;

            if (outcome.WasIngested)
            {
                Ingested++;
                Chunks += outcome.ChunkCount;
            }

            if (outcome.WasUnchanged) { Unchanged++; }
            if (outcome.WasFailed) { Failed++; }
            if (outcome.WasSkipped) { Skipped++; }
        }

        public IngestionReport ToReport() =>
            new(Attempted, Ingested, Unchanged, Failed, Skipped, Chunks, BudgetExhausted, RateLimited);
    }

    [LoggerMessage(LogLevel.Information, "Indexed {SourceType}:{NaturalKey} - {ChunkCount} chunk(s), {ImageCount} image(s).")]
    static partial void LogIngested(ILogger logger, string sourceType, string naturalKey, int chunkCount, int imageCount);

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
