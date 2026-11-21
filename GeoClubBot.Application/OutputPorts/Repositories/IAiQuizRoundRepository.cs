using Entities;

namespace UseCases.OutputPorts.Repositories;

/// <summary>One member's quiz record over a period: points, right answers, answers.</summary>
public sealed record QuizScoreLine(ulong UserId, int Points, int Right, int Answered);

public interface IAiQuizRoundRepository
{
    void Add(AiQuizRound round);

    /// <summary>A round with its guesses, tracked for changes; null when there is none.</summary>
    Task<AiQuizRound?> ReadAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Rounds whose answer is due to be shown and is not yet, with their guesses, tracked; oldest first.</summary>
    Task<IReadOnlyList<AiQuizRound>> ReadDueAsync(DateTimeOffset nowUtc, int max, CancellationToken cancellationToken = default);

    /// <summary>Every member's record since <paramref name="sinceUtc"/> (all time when null), best first.</summary>
    Task<IReadOnlyList<QuizScoreLine>> ReadScoresAsync(DateTimeOffset? sinceUtc, CancellationToken cancellationToken = default);
}
