using Configuration;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.AI.Ingestion;

/// <summary>
/// Queues indexed sources whose images never made it into the index, so later runs embed them.
///
/// For damage already done. Before image failures were retried, a run that could not embed a source's
/// pictures still recorded it as fully indexed, and unchanged content is never embedded again — so those
/// pictures stayed missing for good. The index is asked rather than the registry, because only the index
/// knows which image chunks lack a vector.
/// </summary>
public sealed record BackfillMissingImagesCommand : ICommand<Result<ImageBackfillReport>>;

/// <param name="SourcesWithMissingImages">Sources the index reports as holding images without a vector.</param>
/// <param name="Queued">How many of those were queued; the rest were already queued or are not indexed.</param>
public sealed record ImageBackfillReport(int SourcesWithMissingImages, int Queued);

public sealed partial class BackfillMissingImagesHandler(
    IKnowledgeIndex knowledgeIndex,
    IKnowledgeSourceRepository sources,
    IOptions<AiIngestionConfiguration> ingestionConfiguration,
    ILogger<BackfillMissingImagesHandler> logger)
    : IRequestHandler<BackfillMissingImagesCommand, Result<ImageBackfillReport>>
{
    public async Task<Result<ImageBackfillReport>> Handle(
        BackfillMissingImagesCommand request,
        CancellationToken cancellationToken)
    {
        if (!ingestionConfiguration.Value.EmbedImages)
        {
            // A queued source would be re-indexed text-only again, spending requests to change nothing.
            return Error.Validation("ai.image_embedding_disabled",
                "Image embedding is turned off (AI:Ingestion:EmbedImages), so there is nothing to backfill.");
        }

        var missing = await knowledgeIndex.ReadSourcesMissingImageVectorsAsync(cancellationToken)
            .ConfigureAwait(false);

        if (missing.Count == 0)
        {
            return new ImageBackfillReport(0, 0);
        }

        var wanted = missing
            .Select(key => BuildKey(key.SourceType, key.SourceKey))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var queued = 0;
        foreach (var source in await sources.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (wanted.Contains(BuildKey(source.SourceType, source.NaturalKey)) && source.RequestImageBackfill())
            {
                queued++;
            }
        }

        LogQueued(logger, missing.Count, queued);
        return new ImageBackfillReport(missing.Count, queued);
    }

    private static string BuildKey(string sourceType, string naturalKey) => $"{sourceType}|{naturalKey}";

    [LoggerMessage(LogLevel.Information,
        "Image backfill: {Missing} source(s) hold images without a vector; {Queued} queued for re-indexing.")]
    static partial void LogQueued(ILogger logger, int missing, int queued);
}
