using Entities;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Ingestion;
using UseCases.UseCases.AI.Retrieval;

namespace UseCases.UseCases.AI.KnowledgeGaps;

/// <param name="Country">The one country the question names, as the index files it first; null when it names none or several.</param>
/// <param name="IndexNames">Every spelling the index files that country under.</param>
/// <param name="Topic">What the question is about (poles, bollards, …), as the ingestion's classifier reads it; null when none.</param>
public sealed record QuestionSubject(string? Country, IReadOnlyList<string> IndexNames, string? Topic);

/// <summary>
/// Whether a question's retrieval was weak, and what it was about. Pure, so the rules are pinned by tests.
///
/// Three signs, in order: the index offered nothing; the question names one country and nothing
/// offered is from it (a guide about that country is what is missing); or the answer cited none of the
/// excerpts (they were offered, and none was worth citing). A score bar would be a fourth, but fused
/// scores are ranks, not similarities, and say nothing across questions.
/// </summary>
public static class WeakRetrieval
{
    public static QuestionSubject Subject(string? question, CountryGazetteer gazetteer)
    {
        ArgumentNullException.ThrowIfNull(gazetteer);

        var mentions = gazetteer.FindMentions(question);
        var country = mentions.Count == 1 ? mentions[0] : null;
        var topic = string.IsNullOrWhiteSpace(question) ? null : ChunkTopicClassifier.Classify(question);

        return new QuestionSubject(country?.Name, country?.IndexNames ?? [], topic);
    }

    /// <summary>The reason the retrieval was weak, or null when it was not.</summary>
    /// <param name="offered">The excerpts offered with the question, carried ones included.</param>
    /// <param name="cited">How many excerpts the answer cited.</param>
    public static AiRetrievalGapReason? Judge(QuestionSubject subject, IReadOnlyList<KnowledgeHit> offered, int cited)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(offered);

        if (offered.Count == 0)
        {
            return AiRetrievalGapReason.NoExcerpts;
        }

        if (subject.Country is not null
            && !offered.Any(hit => hit.Country is { } country && subject.IndexNames.Contains(country, StringComparer.OrdinalIgnoreCase)))
        {
            return AiRetrievalGapReason.NothingFromTheCountry;
        }

        return cited == 0 ? AiRetrievalGapReason.NothingCited : null;
    }
}
