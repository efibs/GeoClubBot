using System.Collections.Concurrent;

namespace UseCases.UseCases.AI.Quiz;

/// <param name="Streak">Right answers in a row, now.</param>
/// <param name="Best">The longest streak since the bot started.</param>
/// <param name="Right">Right answers in all.</param>
/// <param name="Answered">Questions answered in all.</param>
public sealed record QuizScore(int Streak, int Best, int Right, int Answered)
{
    public static readonly QuizScore None = new(0, 0, 0, 0);
}

/// <summary>
/// Each member's quiz score, in memory: right answers in a row and in all, since the bot started. A
/// quiz is practice, not a league; a restart that forgets the scores costs nothing worth a table.
/// </summary>
public sealed class QuizStreaks
{
    private readonly ConcurrentDictionary<ulong, QuizScore> _scores = new();

    /// <summary>Records an answer the member judged for themselves, and returns the score it makes.</summary>
    public QuizScore Record(ulong userId, bool right) =>
        _scores.AddOrUpdate(
            userId,
            _ => right ? new QuizScore(1, 1, 1, 1) : new QuizScore(0, 0, 0, 1),
            (_, score) =>
            {
                var streak = right ? score.Streak + 1 : 0;
                return new QuizScore(streak, Math.Max(score.Best, streak), score.Right + (right ? 1 : 0), score.Answered + 1);
            });

    public QuizScore Read(ulong userId) => _scores.GetValueOrDefault(userId, QuizScore.None);
}
