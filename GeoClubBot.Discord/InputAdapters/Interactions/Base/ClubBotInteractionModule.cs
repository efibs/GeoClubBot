using Discord.Interactions;
using MediatR;
using Microsoft.Extensions.Logging;

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
        catch (Exception ex)
        {
            Logger.LogError(ex, "Slash command {Module}.{Method} failed", GetType().Name, body.Method.Name);

            var message = failureMessage
                ?? "Something went wrong. Please try again later. If the issue persists, contact an admin.";

            try
            {
                await FollowupAsync(message, ephemeral: true).ConfigureAwait(false);
            }
            catch (Exception followupEx)
            {
                Logger.LogError(followupEx, "Failed to deliver followup error message for {Module}", GetType().Name);
            }
        }
    }
}
