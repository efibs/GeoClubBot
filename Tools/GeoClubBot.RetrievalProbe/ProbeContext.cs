using Infrastructure.OutputAdapters.AI;
using Qdrant.Client;

namespace GeoClubBot.RetrievalProbe;

/// <summary>Everything a command needs, set up once by <c>Program</c>.</summary>
/// <param name="Qdrant">Guarded: only reads reach the index (<see cref="ReadOnlyQdrantInterceptor"/>).</param>
/// <param name="Index">The bot's own index adapter over that guarded client.</param>
/// <param name="Embeddings">Created on first use, so a command that embeds nothing needs no key.</param>
public sealed record ProbeContext(
    ProbeArguments Arguments,
    ProbeSettings Settings,
    QdrantClient Qdrant,
    QdrantKnowledgeIndex Index,
    Func<EmbeddingCache> Embeddings,
    ProbeReport Report,
    CancellationToken CancellationToken)
{
    /// <summary>The rated answers of the export named on the command line, filtered by --rating.</summary>
    public IReadOnlyList<RatedQuestion> ReadRatedQuestions()
    {
        if (Arguments.Target is not { Length: > 0 } path)
        {
            throw new ProbeException($"{Arguments.Command} needs an export file: {Arguments.Command} <export.jsonl>");
        }

        var questions = FeedbackExport.Read(path);
        if (Arguments.Rating is { } rating)
        {
            questions = [.. questions.Where(question =>
                string.Equals(question.Record.Rating, rating, StringComparison.OrdinalIgnoreCase))];
        }

        return questions.Count > 0
            ? questions
            : throw new ProbeException("No rated answer in the export matched.");
    }
}
