using Configuration;
using Constants;
using Discord;
using Discord.Interactions;
using Entities;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.UseCases.AI.Feedback;

namespace GeoClubBot.Discord.InputAdapters.Interactions.AI;

/// <summary>
/// The written-feedback path: right-click an answer → Apps → a verdict, then an optional comment.
///
/// Two entries rather than one plus a rating picker. A message command must take exactly one
/// IMessage parameter, and modals carry no select menus, so a single entry would need an extra
/// ephemeral hop before the comment box. Two reach it in one click with an unambiguous verdict.
///
/// Deliberately outside the /ai group: context commands are not nested, and grouping them here would
/// only risk the group prefix leaking into the custom ids.
/// </summary>
[CommandContextType(InteractionContextType.Guild)]
public partial class AiFeedbackModule(
    ISender mediator,
    IOptions<AiConfiguration> aiConfiguration,
    IOptions<AiFeedbackConfiguration> feedbackConfiguration,
    ILogger<AiFeedbackModule> logger)
    : ClubBotInteractionModule(mediator, logger)
{
    [MessageCommand("👍 Good AI answer")]
    public Task RateGoodAsync(IMessage message) => OpenCommentModalAsync(message, AiFeedbackRating.Positive);

    [MessageCommand("👎 Bad AI answer")]
    public Task RateBadAsync(IMessage message) => OpenCommentModalAsync(message, AiFeedbackRating.Negative);

    [ModalInteraction($"{ComponentIds.AiFeedbackCommentModalId}:*,*", true)]
    public Task CommentSubmittedAsync(string ratingText, string messageIdText, AiFeedbackCommentModal modal) =>
        ExecuteAsync(async ct =>
        {
            if (!Enum.TryParse<AiFeedbackRating>(ratingText, out var rating)
                || !ulong.TryParse(messageIdText, out var messageId))
            {
                await FollowupAsync("That feedback form has expired.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            var result = await Mediator
                .Send(new SubmitAiAnswerFeedbackCommand(messageId, Context.User.Id, rating, modal.Comment), ct)
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                await FollowupFailureAsync(result.Error).ConfigureAwait(false);
                return;
            }

            await FollowupAsync(Acknowledgement(rating, result.Value), ephemeral: true).ConfigureAwait(false);
        }, ephemeral: true, failureMessage: "Failed to record your feedback.");

    /// <summary>
    /// Opens the comment box. Not routed through <see cref="ClubBotInteractionModule.ExecuteAsync"/>,
    /// which defers first — a modal has to be the interaction's immediate response.
    /// </summary>
    private async Task OpenCommentModalAsync(IMessage message, AiFeedbackRating rating)
    {
        try
        {
            // The interactions assembly is scanned whether or not the feature is on, so these two
            // commands exist on a bot with AI switched off and have to say so themselves.
            if (!aiConfiguration.Value.Active || !feedbackConfiguration.Value.Enabled)
            {
                await RespondAsync("AI features are not active.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            var modalId = $"{ComponentIds.AiFeedbackCommentModalId}:{rating},{message.Id}";

            await Context.Interaction.RespondWithModalAsync<AiFeedbackCommentModal>(modalId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogFeedbackModalOpenFailed(Logger, ex, message.Id);
            await RespondAsync("Failed to open the feedback form.", ephemeral: true).ConfigureAwait(false);
        }
    }

    private static string Acknowledgement(AiFeedbackRating rating, AiFeedbackOutcome outcome)
    {
        var verdict = rating == AiFeedbackRating.Positive ? "👍" : "👎";

        return outcome.WasCreated
            ? $"Thanks — recorded {verdict}. The conversation has been saved so it can be reviewed."
            : $"Updated to {verdict}.";
    }

    [LoggerMessage(LogLevel.Error, "Failed to open the AI feedback modal for message {MessageId}.")]
    static partial void LogFeedbackModalOpenFailed(ILogger logger, Exception ex, ulong messageId);
}
