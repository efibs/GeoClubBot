using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Conversations;
using Utilities;

namespace UseCases.UseCases.AI.Feedback;

/// <param name="RatedDiscordMessageId">
/// The answer's own message, which is not necessarily the one that was reacted to: a long answer is
/// posted as several messages and any of them resolves to the same turn. The adapter reacts here
/// rather than where the click landed, so the confirmation sits on the message the archive names.
/// </param>
/// <param name="WasCreated">False when an existing verdict was amended rather than a new one stored.</param>
/// <param name="RatingsOnMessage">
/// How many verdicts the answer carries once this one is committed. The adapter uses it to decide
/// whether the confirmation reaction belongs on the message, which is a property of the answer
/// rather than of any one reviewer.
/// </param>
public sealed record AiFeedbackOutcome(
    ulong RatedDiscordMessageId,
    bool WasCreated,
    int RatingsOnMessage);

/// <summary>
/// Records a verdict on one AI answer and, the first time, archives the conversation that produced it.
///
/// This is the only path by which a conversation is stored permanently. Ordinary turns are swept on
/// a retention schedule because keeping what people asked is a privacy posture, not an archive; a
/// branch graduates to being kept only when somebody explicitly rates it.
/// </summary>
public sealed record SubmitAiAnswerFeedbackCommand(
    ulong RatedDiscordMessageId,
    ulong ReviewerDiscordUserId,
    AiFeedbackRating Rating,
    string? Comment) : ICommand<Result<AiFeedbackOutcome>>;

public sealed class SubmitAiAnswerFeedbackHandler(
    IAiConversationRepository conversations,
    IAiFeedbackRepository feedbacks,
    IOptions<AiFeedbackConfiguration> configuration)
    : IRequestHandler<SubmitAiAnswerFeedbackCommand, Result<AiFeedbackOutcome>>
{
    public async Task<Result<AiFeedbackOutcome>> Handle(
        SubmitAiAnswerFeedbackCommand request,
        CancellationToken cancellationToken)
    {
        if (!configuration.Value.Enabled)
        {
            return Error.Conflict("ai.feedback.disabled", "Feedback on my answers is switched off.");
        }

        // The cheap gate. Reactions arrive for every message in the channel, and this is one indexed
        // probe — no Discord round-trip, no conversation load.
        var answer = await conversations
            .ReadAssistantTurnByAnyMessageIdAsync(request.RatedDiscordMessageId, cancellationToken)
            .ConfigureAwait(false);

        if (answer is null)
        {
            // Not ours at all, a question rather than an answer, or history that has aged out. The
            // three are indistinguishable to whoever clicked, and the advice is the same.
            return Error.NotFound("ai.feedback.not_an_answer",
                "That isn't one of my answers — or it's older than my history window.");
        }

        // Everything downstream keys off the turn's own message rather than whatever was clicked.
        // Without this, rating chunk 1 and then chunk 3 of the same answer would look like two
        // different answers to the lookup and collide on the unique index at commit time.
        var ratedMessageId = answer.DiscordMessageId;

        var ratingsOnMessage = await feedbacks
            .CountForMessageAsync(ratedMessageId, cancellationToken)
            .ConfigureAwait(false);

        var existing = await feedbacks
            .ReadForUpdateAsync(ratedMessageId, request.ReviewerDiscordUserId, cancellationToken)
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;

        if (existing is not null)
        {
            existing.ChangeRating(request.Rating, now);

            // Only overwritten when text was actually supplied: the reaction path carries no comment,
            // and a later 👍 must not silently erase the explanation someone typed earlier.
            if (!string.IsNullOrWhiteSpace(request.Comment))
            {
                existing.AttachComment(request.Comment, now);
            }

            return new AiFeedbackOutcome(ratedMessageId, WasCreated: false, ratingsOnMessage);
        }

        var turns = await conversations
            .ReadConversationAsync(answer.ConversationId, cancellationToken)
            .ConfigureAwait(false);

        var branch = ConversationContextBuilder.BuildBranch(turns, ratedMessageId);
        if (branch.Count == 0)
        {
            // The answer resolved but its tree did not — a partially swept conversation. Archiving an
            // answer with no question attached would put an unusable record in the export.
            return Error.NotFound("ai.feedback.conversation_gone",
                "I can't find the conversation that answer belongs to any more.");
        }

        feedbacks.Add(AiAnswerFeedback.Create(
            branch, request.ReviewerDiscordUserId, request.Rating, request.Comment, now));

        // Counted from before the insert: the unit of work has not committed yet, so a re-count here
        // would not see the row this command is adding.
        return new AiFeedbackOutcome(ratedMessageId, WasCreated: true, ratingsOnMessage + 1);
    }
}
