using Discord.Interactions;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Utilities;

namespace GeoClubBot.Discord.InputAdapters.Interactions.Base;

/// <summary>
/// Base for slash-command modules. Standardises the
/// "defer → run body → friendly followup on failure" flow so each command can shrink
/// to a single ExecuteAsync(...) call.
/// </summary>
public abstract class ClubBotInteractionModule(ISender mediator, ILogger logger)
    : InteractionModuleBase<SocketInteractionContext>
{
    protected ISender Mediator { get; } = mediator;

    protected ILogger Logger { get; } = logger;

    /// <summary>
    /// Defers the current interaction, runs <paramref name="body"/>, and sends a friendly
    /// followup if it throws. Any exception is logged and swallowed so Discord callers
    /// always see a response.
    /// </summary>
    protected async Task ExecuteAsync(
        Func<CancellationToken, Task> body,
        bool ephemeral = false,
        string? failureMessage = null)
    {
        await DeferAsync(ephemeral).ConfigureAwait(false);

        try
        {
            await body(CancellationToken.None).ConfigureAwait(false);
        }
        catch (ValidationException validationEx)
        {
            // Bubble up the actual field-level messages from the validator so the user
            // can correct the bad input. ValidationBehavior in the MediatR pipeline throws
            // this when an AbstractValidator<T> fails.
            Logger.LogInformation(
                "Slash command {Module}.{Method} rejected by validation: {Errors}",
                GetType().Name, body.Method.Name,
                string.Join("; ", validationEx.Errors.Select(e => e.ErrorMessage)));

            var message = string.Join("\n", validationEx.Errors.Select(e => $"• {e.ErrorMessage}"));
            await TrySendFollowupAsync(message).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Slash command {Module}.{Method} failed", GetType().Name, body.Method.Name);

            var message = failureMessage
                ?? "Something went wrong. Please try again later. If the issue persists, contact an admin.";
            await TrySendFollowupAsync(message).ConfigureAwait(false);
        }
    }

    private async Task TrySendFollowupAsync(string message)
    {
        try
        {
            await FollowupAsync(message, ephemeral: true).ConfigureAwait(false);
        }
        catch (Exception followupEx)
        {
            Logger.LogError(followupEx, "Failed to deliver followup error message for {Module}", GetType().Name);
        }
    }

    /// <summary>
    /// Sends a friendly followup for a Result&lt;T&gt;.Failure, derived from the error's
    /// <see cref="ErrorType"/>. Use after a Mediator.Send(...) that returns a Result.
    /// </summary>
    protected Task FollowupFailureAsync(Error error, bool ephemeral = true) =>
        FollowupAsync(FriendlyMessageFor(error), ephemeral: ephemeral);

    public static string FriendlyMessageFor(Error error) => error.Type switch
    {
        ErrorType.NotFound => string.IsNullOrEmpty(error.Message) ? "The requested item was not found." : error.Message,
        ErrorType.Validation => string.IsNullOrEmpty(error.Message) ? "The request was invalid." : error.Message,
        ErrorType.Conflict => string.IsNullOrEmpty(error.Message) ? "The request conflicts with the current state." : error.Message,
        ErrorType.Forbidden => "You do not have permission to do that.",
        ErrorType.Unauthorized => "You must be authenticated to do that.",
        _ => "Something went wrong. Please try again later. If the issue persists, contact an admin."
    };
}
