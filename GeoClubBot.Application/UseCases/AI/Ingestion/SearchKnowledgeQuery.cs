using MediatR;
using UseCases.Abstractions;
using UseCases.OutputPorts.AI;
using Utilities;

namespace UseCases.UseCases.AI.Ingestion;

/// <summary>
/// Runs retrieval without generating an answer.
///
/// Exists as an operator tool: it costs one embedding request instead of the two a full question
/// costs, and it shows exactly what the model would have been given. With a small daily allowance
/// that makes it the only practical way to debug retrieval quality.
/// </summary>
public sealed record SearchKnowledgeQuery(string Query, string? Country = null, int Limit = 8)
    : IQuery<Result<IReadOnlyList<KnowledgeHit>>>;

public sealed class SearchKnowledgeHandler(IEmbedder embedder, IKnowledgeIndex knowledgeIndex)
    : IRequestHandler<SearchKnowledgeQuery, Result<IReadOnlyList<KnowledgeHit>>>
{
    public async Task<Result<IReadOnlyList<KnowledgeHit>>> Handle(
        SearchKnowledgeQuery request,
        CancellationToken cancellationToken)
    {
        var embedded = await embedder
            .EmbedAsync([new TextEmbeddingInput(request.Query)], cancellationToken)
            .ConfigureAwait(false);

        if (embedded.IsFailure)
        {
            return embedded.Error;
        }

        var hits = await knowledgeIndex.SearchAsync(
            new KnowledgeQuery
            {
                TextVector = embedded.Value[0],
                Country = request.Country,
                Limit = request.Limit
            },
            cancellationToken).ConfigureAwait(false);

        return Result<IReadOnlyList<KnowledgeHit>>.Success(hits);
    }
}
