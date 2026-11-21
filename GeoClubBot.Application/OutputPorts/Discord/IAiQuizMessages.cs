using Entities;

namespace UseCases.OutputPorts.Discord;

/// <summary>The quiz's posted questions, as the reveal job reaches them without an interaction (#491).</summary>
public interface IAiQuizMessages
{
    /// <summary>Edits a round's message to show the answer, the explanation and how everyone did; false when it could not.</summary>
    Task<bool> ShowAnswerAsync(AiQuizRound round, CancellationToken cancellationToken = default);
}
