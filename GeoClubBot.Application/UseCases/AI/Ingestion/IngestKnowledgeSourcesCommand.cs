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
public sealed record IngestionReport(
    int Attempted,
    int Ingested,
    int Unchanged,
    int Failed,
    int Skipped,
    int ChunksWritten,
    bool BudgetExhausted);

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
            if (written.Error.Code == BudgetExhaustedCode)
            {
                // Not the source's fault: leave its state untouched so it is retried first next run.
                return Outcome.OutOfBudget;
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

        return Outcome.Ingested(written.Value.ChunkCount);
    }

    private async Task<Result<WriteResult>> WriteAsync(
        KnowledgeSource source,
        SourceDescriptor descriptor,
        IReadOnlyList<ExtractedChunk> chunks,
        string ingestRun,
        AiIngestionConfiguration settings,
        CancellationToken cancellationToken)
    {
        // Rewritten before anything else uses the URL, so the embedder and the eventual Discord embed
        // both point at the same fetchable image.
        chunks = await RelayImagesAsync(chunks, cancellationToken).ConfigureAwait(false);

        var textInputs = chunks
            .Select(chunk => (EmbeddingInput)new TextEmbeddingInput(BuildEmbeddingText(descriptor, chunk)))
            .ToList();

        if (!await TryReserveAsync(textInputs.Count, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict(BudgetExhaustedCode, "Indexing has used its share of today's AI allowance.");
        }

        var textVectors = await embedder.EmbedAsync(textInputs, cancellationToken).ConfigureAwait(false);
        if (textVectors.IsFailure)
        {
            return textVectors.Error;
        }

        var imageVectors = await EmbedImagesAsync(chunks, settings, cancellationToken).ConfigureAwait(false);

        var points = new List<KnowledgePoint>(chunks.Count);
        for (var index = 0; index < chunks.Count; index++)
        {
            var chunk = chunks[index];

            // Written out rather than folded into the constructor call: an image chunk whose image
            // could not be embedded must carry no image vector at all, and that distinction is too
            // easy to lose in a conditional expression.
            ReadOnlyMemory<float>? imageVector = null;
            if (chunk.ImageUrl is { } imageUrl && imageVectors.Vectors.TryGetValue(imageUrl, out var embedded))
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

        return new WriteResult(points.Count, imageVectors.Vectors.Count, imageVectors.DeferredForBudget);
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
    /// Embeds images separately from text, and tolerates total failure.
    ///
    /// The provider fetches image URLs server-side, and one unreachable URL fails the whole batch —
    /// several guide sites block unattended clients. Isolating images means a blocked host costs
    /// image search for that source rather than removing the source from the index entirely.
    /// </summary>
    private async Task<ImageEmbeddingResult> EmbedImagesAsync(
        IReadOnlyList<ExtractedChunk> chunks,
        AiIngestionConfiguration settings,
        CancellationToken cancellationToken)
    {
        var empty = ImageEmbeddingResult.None;
        if (!settings.EmbedImages)
        {
            return empty;
        }

        var imageUrls = chunks
            .Select(chunk => chunk.ImageUrl)
            .Where(url => url is not null)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (imageUrls.Count == 0)
        {
            return empty;
        }

        if (!await TryReserveAsync(imageUrls.Count, cancellationToken).ConfigureAwait(false))
        {
            // The text is already written, so the source stays usable — but it is only half indexed,
            // and saying so is what brings it back for its images instead of leaving them lost until
            // someone forces a rebuild.
            LogImagesDeferred(logger, imageUrls.Count);
            return ImageEmbeddingResult.Deferred;
        }

        var inputs = imageUrls.Select(url => (EmbeddingInput)new ImageEmbeddingInput(url!)).ToList();
        var vectors = await embedder.EmbedAsync(inputs, cancellationToken).ConfigureAwait(false);

        if (vectors.IsFailure)
        {
            LogImageEmbeddingFailed(logger, imageUrls.Count, vectors.Error.Message);
            return empty;
        }

        var result = new Dictionary<string, ReadOnlyMemory<float>>(StringComparer.Ordinal);
        for (var index = 0; index < imageUrls.Count; index++)
        {
            result[imageUrls[index]!] = vectors.Value[index];
        }

        return new ImageEmbeddingResult(result, DeferredForBudget: false);
    }

    /// <summary>Claims the embedding requests this batch will cost before spending them.</summary>
    private async Task<bool> TryReserveAsync(int inputCount, CancellationToken cancellationToken)
    {
        var batchSize = Math.Max(1, aiConfiguration.Value.OpenRouter.EmbeddingBatchSize);
        var requests = (int)Math.Ceiling(inputCount / (double)batchSize);

        return await budget.TryReserveRequestsAsync(
            DateOnly.FromDateTime(DateTime.UtcNow),
            requests,
            ReadIngestionDailyCap(),
            cancellationToken).ConfigureAwait(false);
    }

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

    /// <summary>
    /// Prefixes each chunk with its country and section before embedding, while the stored text stays
    /// bare. It is the cheapest way to put the country name inside every vector, so "Tunisian
    /// bollards" matches a paragraph that only ever says "the bollards here".
    /// </summary>
    private static string BuildEmbeddingText(SourceDescriptor descriptor, ExtractedChunk chunk)
    {
        var header = string.Join(" — ", new[] { descriptor.Country, chunk.SectionPath }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

        return header.Length == 0 ? chunk.Text : $"{header}\n\n{chunk.Text}";
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

    private sealed record WriteResult(int ChunkCount, int ImageCount, bool ImagesDeferred);

    /// <param name="DeferredForBudget">
    /// Distinguishes "ran out of allowance" from "the images could not be embedded". Only the former
    /// is worth retrying; a permanently blocked image host would otherwise be retried every run.
    /// </param>
    private sealed record ImageEmbeddingResult(
        Dictionary<string, ReadOnlyMemory<float>> Vectors,
        bool DeferredForBudget)
    {
        public static ImageEmbeddingResult None =>
            new(new Dictionary<string, ReadOnlyMemory<float>>(StringComparer.Ordinal), false);

        public static ImageEmbeddingResult Deferred =>
            new(new Dictionary<string, ReadOnlyMemory<float>>(StringComparer.Ordinal), true);
    }

    private readonly record struct Outcome(
        bool WasIngested,
        bool WasUnchanged,
        bool WasFailed,
        bool WasSkipped,
        bool BudgetExhausted,
        int ChunkCount)
    {
        public static Outcome Ingested(int chunkCount) => new(true, false, false, false, false, chunkCount);
        public static readonly Outcome Unchanged = new(false, true, false, false, false, 0);
        public static readonly Outcome Failed = new(false, false, true, false, false, 0);
        public static readonly Outcome Skipped = new(false, false, false, true, false, 0);
        public static readonly Outcome OutOfBudget = new(false, false, false, false, true, 0);
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

        public void Apply(Outcome outcome)
        {
            if (outcome.BudgetExhausted)
            {
                BudgetExhausted = true;
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
            new(Attempted, Ingested, Unchanged, Failed, Skipped, Chunks, BudgetExhausted);
    }

    [LoggerMessage(LogLevel.Information, "Indexed {SourceType}:{NaturalKey} - {ChunkCount} chunk(s), {ImageCount} image(s).")]
    static partial void LogIngested(ILogger logger, string sourceType, string naturalKey, int chunkCount, int imageCount);

    [LoggerMessage(LogLevel.Warning, "Could not embed {ImageCount} image(s); indexing text only ({Reason}).")]
    static partial void LogImageEmbeddingFailed(ILogger logger, int imageCount, string reason);

    [LoggerMessage(LogLevel.Information, "Postponed {ImageCount} image(s) until the allowance resets.")]
    static partial void LogImagesDeferred(ILogger logger, int imageCount);

    [LoggerMessage(LogLevel.Information,
        "Stopping ingestion after {Attempted} source(s): indexing has used its share of today's AI allowance.")]
    static partial void LogBudgetExhausted(ILogger logger, int attempted);
}
