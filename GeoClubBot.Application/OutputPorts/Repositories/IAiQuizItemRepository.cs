using Entities;

namespace UseCases.OutputPorts.Repositories;

/// <param name="Kind">The kind of question wanted.</param>
/// <param name="Difficulties">The difficulties allowed.</param>
/// <param name="Version">Only ratings of this generation.</param>
/// <param name="Countries">Every spelling the index files the country under; null for any country.</param>
/// <param name="ExcludedSourceType">A guide family to leave out (PlonkIt, for a lesser-known quiz); null for none.</param>
public sealed record QuizItemDraw(
    AiQuizQuestionKind Kind,
    IReadOnlyCollection<AiQuizDifficulty> Difficulties,
    int Version,
    IReadOnlyCollection<string>? Countries = null,
    string? ExcludedSourceType = null);

public interface IAiQuizItemRepository
{
    /// <summary>
    /// Rated pictures that make the question asked, with their questions: those never asked first, then
    /// those asked longest ago, at random among equals.
    /// </summary>
    Task<IReadOnlyList<AiQuizItem>> DrawAsync(QuizItemDraw draw, int count, CancellationToken cancellationToken = default);

    /// <summary>For each of the chunks rated in this generation, the content hash its rating was made of.</summary>
    Task<IReadOnlyDictionary<Guid, string>> ReadContentHashesAsync(
        IReadOnlyCollection<Guid> chunkIds,
        int version,
        CancellationToken cancellationToken = default);

    /// <summary>Stores a rating in place of any earlier one of the same picture.</summary>
    Task ReplaceAsync(AiQuizItem item, CancellationToken cancellationToken = default);

    /// <summary>Forgets the ratings of pictures no longer in the index, or changed since.</summary>
    Task RemoveAsync(IReadOnlyCollection<Guid> chunkIds, CancellationToken cancellationToken = default);

    /// <summary>Pictures rated in this generation, and how many of them make at least one question.</summary>
    Task<(int Rated, int WithQuestions)> CountAsync(int version, CancellationToken cancellationToken = default);
}
