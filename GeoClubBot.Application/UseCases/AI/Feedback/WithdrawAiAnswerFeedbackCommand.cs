using Entities;
using MediatR;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.AI.Feedback;

/// <summary>
/// Takes back a verdict, deleting the archived conversation with it — the conversation was only kept
/// because of the rating, so withdrawing the rating withdraws consent to store it.
/// </summary>
/// <param name="Rating">
/// Which verdict is being taken back. It matters because both reactions can sit on the same message:
/// a user who reacted 👍 and then 👎 has a stored verdict of 👎, and tidying away the stale 👍 must
/// not delete the rating they actually meant.
/// </param>
public sealed record WithdrawAiAnswerFeedbackCommand(
    ulong RatedDiscordMessageId,
    ulong ReviewerDiscordUserId,
    AiFeedbackRating Rating) : ICommand<Result<AiFeedbackOutcome>>;

public sealed class WithdrawAiAnswerFeedbackHandler(
    IAiConversationRepository conversations,
    IAiFeedbackRepository feedbacks)
    : IRequestHandler<WithdrawAiAnswerFeedbackCommand, Result<AiFeedbackOutcome>>
{
    public async Task<Result<AiFeedbackOutcome>> Handle(
        WithdrawAiAnswerFeedbackCommand request,
        CancellationToken cancellationToken)
    {
        // Same normalisation as submitting: an earlier part of a split answer must find the verdict
        // that was stored against the answer's own message. Falls back to the clicked message when
        // the turn is gone, which is the right answer when it was never an alias to begin with.
        var answer = await conversations
            .ReadAssistantTurnByAnyMessageIdAsync(request.RatedDiscordMessageId, cancellationToken)
            .ConfigureAwait(false);

        var ratedMessageId = answer?.DiscordMessageId ?? request.RatedDiscordMessageId;

        var ratingsOnMessage = await feedbacks
            .CountForMessageAsync(ratedMessageId, cancellationToken)
            .ConfigureAwait(false);

        var existing = await feedbacks
            .ReadForUpdateAsync(ratedMessageId, request.ReviewerDiscordUserId, cancellationToken)
            .ConfigureAwait(false);

        // Removing a reaction that was never stored as feedback is ordinary channel noise, not an
        // error: reactions on non-AI messages, on answers that aged out, and stale duplicates all
        // land here.
        if (existing is null || existing.Rating != request.Rating)
        {
            return new AiFeedbackOutcome(ratedMessageId, WasCreated: false, ratingsOnMessage);
        }

        feedbacks.Remove(existing);

        // Counted from before the delete, which the unit of work has not committed yet.
        return new AiFeedbackOutcome(ratedMessageId, WasCreated: false, Math.Max(0, ratingsOnMessage - 1));
    }
}
