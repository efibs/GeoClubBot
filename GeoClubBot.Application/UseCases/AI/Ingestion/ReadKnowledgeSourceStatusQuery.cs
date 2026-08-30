using MediatR;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.AI.Ingestion;

/// <summary>Counts of indexed sources by state, for operator-facing status.</summary>
public sealed record ReadKnowledgeSourceStatusQuery : IQuery<Result<KnowledgeSourceCounts>>;

public sealed class ReadKnowledgeSourceStatusHandler(IKnowledgeSourceRepository sources)
    : IRequestHandler<ReadKnowledgeSourceStatusQuery, Result<KnowledgeSourceCounts>>
{
    public async Task<Result<KnowledgeSourceCounts>> Handle(
        ReadKnowledgeSourceStatusQuery request,
        CancellationToken cancellationToken) =>
        await sources.CountByStatusAsync(cancellationToken).ConfigureAwait(false);
}
