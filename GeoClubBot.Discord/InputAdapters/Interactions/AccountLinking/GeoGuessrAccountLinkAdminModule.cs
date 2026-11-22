using Constants;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Entities;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using GeoClubBot.Discord.InputAdapters.Interactions.Help;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.UseCases.GeoGuessrAccountLinking;
using Utilities;

namespace GeoClubBot.Discord.InputAdapters.Interactions.AccountLinking;

[CommandContextType(InteractionContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.Administrator)]
[HelpFeature("linking")]
[Group("gg-account-link-admin", "Complete, cancel or undo members' account links")]
public partial class GeoGuessrAccountLinkAdminModule(
    ISender mediator,
    ILogger<GeoGuessrAccountLinkAdminModule> logger) : ClubBotInteractionModule(mediator, logger)
{
    [SlashCommand("complete", "Complete a member's account link with the one-time password they sent you")]
    [HelpDetail("Usually quicker with the buttons on the request I post for admins.")]
    public Task CompleteAccountLinkingProcessAsync(
        [Summary(description: "The member who asked to link")] IUser discordUser,
        [Summary(description: "Their GeoGuessr user id: the end of their profile link")] string geoGuessrUserId,
        [Summary(description: "The password they sent you inside GeoGuessr")] string oneTimePassword) =>
        ExecuteAsync(
            async ct =>
            {
                var result = await Mediator
                    .Send(new CompleteAccountLinkingCommand(discordUser.Id, geoGuessrUserId, oneTimePassword), ct)
                    .ConfigureAwait(false);

                await HandleLinkingEndedAsync(result, discordUser, null).ConfigureAwait(false);
            },
            ephemeral: true,
            failureMessage: "Failed to complete linking process (internal error).");

    [SlashCommand("cancel", "Cancel a member's pending account link")]
    public Task CancelAccountLinkingProcessAsync(
        [Summary(description: "The member who asked to link")] IUser discordUser,
        [Summary(description: "Their GeoGuessr user id: the end of their profile link")] string geoGuessrUserId) =>
        ExecuteAsync(
            async ct =>
            {
                var result = await Mediator
                    .Send(new CancelAccountLinkingCommand(discordUser.Id, geoGuessrUserId), ct)
                    .ConfigureAwait(false);

                await HandleLinkingCanceledByAdminAsync(result, discordUser, null).ConfigureAwait(false);
            },
            ephemeral: true,
            failureMessage: "Failed to cancel linking process (internal error).");

    [SlashCommand("unlink", "Undo the link between a member's Discord and GeoGuessr accounts")]
    public Task UnlinkAccountsSlashCommandAsync(
        [Summary(description: "The linked member")] IUser discordUser,
        [Summary(description: "Their GeoGuessr user id: the end of their profile link")] string geoGuessrUserId) =>
        ExecuteAsync(
            async ct =>
            {
                var result = await Mediator
                    .Send(new UnlinkAccountsCommand(discordUser.Id, geoGuessrUserId), ct)
                    .ConfigureAwait(false);

                await FollowupAsync(
                        result.IsSuccess
                            ? "Account linking was successfully removed."
                            : FriendlyMessageFor(result.Error),
                        ephemeral: true)
                    .ConfigureAwait(false);
            },
            ephemeral: true,
            failureMessage: "Failed to remove account link.");

    // Button → modal interactions must use RespondWithModalAsync (an immediate response),
    // so they cannot be wrapped in ExecuteAsync, which would defer first.
    [ComponentInteraction($"{ComponentIds.GeoGuessrAccountLinkingCompleteButtonId}:*,*", true)]
    public async Task CompleteLinkingButtonPressedAsync(string discordUserIdString, string geoGuessrUserId)
    {
        try
        {
            var messageId = (Context.Interaction as SocketMessageComponent)?.Message?.Id;
            var modalId =
                $"{ComponentIds.GeoGuessrAccountLinkingCompleteModalId}:{discordUserIdString},{geoGuessrUserId},{messageId}";

            await Context.Interaction.RespondWithModalAsync<CompleteAccountLinkModal>(modalId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogOtpModalOpenFailed(Logger, ex, discordUserIdString, geoGuessrUserId);
            await RespondAsync("Failed to open one time password input modal.", ephemeral: true).ConfigureAwait(false);
        }
    }

    [ModalInteraction($"{ComponentIds.GeoGuessrAccountLinkingCompleteModalId}:*,*,*", true)]
    public Task CompleteLinkingPasswordSubmitted(string discordUserIdString, string geoGuessrUserId, string messageIdString, CompleteAccountLinkModal modal) =>
        ExecuteAsync(
            async ct =>
            {
                var discordUserId = ulong.Parse(discordUserIdString);

                var result = await Mediator
                    .Send(new CompleteAccountLinkingCommand(discordUserId, geoGuessrUserId, modal.OneTimePassword), ct)
                    .ConfigureAwait(false);

                var discordUser = Context.Guild.GetUser(discordUserId);
                await HandleLinkingEndedAsync(result, discordUser, messageIdString).ConfigureAwait(false);
            },
            ephemeral: true,
            failureMessage: "Failed to complete linking process (internal error).");

    [ComponentInteraction($"{ComponentIds.GeoGuessrAccountLinkingCancelButtonId}:*,*", true)]
    public async Task CancelLinkingButtonPressedAsync(string discordUserIdString, string geoGuessrUserId)
    {
        try
        {
            var messageId = (Context.Interaction as SocketMessageComponent)?.Message?.Id;
            var modalId =
                $"{ComponentIds.GeoGuessrAccountLinkingCancelModalId}:{discordUserIdString},{geoGuessrUserId},{messageId}";

            await Context.Interaction.RespondWithModalAsync<CancelAccountLinkModal>(modalId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogCancelModalOpenFailed(Logger, ex, discordUserIdString, geoGuessrUserId);
            await RespondAsync("Failed to open cancel modal.", ephemeral: true).ConfigureAwait(false);
        }
    }

    [ModalInteraction($"{ComponentIds.GeoGuessrAccountLinkingCancelModalId}:*,*,*", true)]
    public Task CancelLinkingSubmitted(string discordUserIdString, string geoGuessrUserId, string messageIdString, CancelAccountLinkModal modal) =>
        ExecuteAsync(
            async ct =>
            {
                if (modal.ConfirmText != "Confirm")
                {
                    await FollowupAsync("Account linking was not canceled. To cancel please enter 'Confirm' into the Confirm Text box.", ephemeral: true).ConfigureAwait(false);
                    return;
                }

                var discordUserId = ulong.Parse(discordUserIdString);

                var result = await Mediator
                    .Send(new CancelAccountLinkingCommand(discordUserId, geoGuessrUserId), ct)
                    .ConfigureAwait(false);

                var discordUser = Context.Guild.GetUser(discordUserId);
                await HandleLinkingCanceledByAdminAsync(result, discordUser, messageIdString).ConfigureAwait(false);
            },
            ephemeral: true,
            failureMessage: "Failed to cancel linking process (internal error).");

    private async Task HandleLinkingEndedAsync(Result<GeoGuessrUser> result, IUser discordUser, string? messageIdString)
    {
        try
        {
            if (result.IsFailure)
            {
                await FollowupAsync(FriendlyMessageFor(result.Error), ephemeral: true).ConfigureAwait(false);
                return;
            }

            await FollowupAsync("Account linking was successful.", ephemeral: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogAdminCompleteResponseFailed(Logger, ex);
        }

        if (result.IsFailure)
        {
            return;
        }

        try
        {
            var dmChannel = await discordUser.CreateDMChannelAsync().ConfigureAwait(false);
            await dmChannel.SendMessageAsync(
                $"Your GeoGuessr account \"{result.Value.Nickname}\" was successfully linked to this Discord account.\n\n" +
                "Type `/help` in the server to see everything you can do now.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogLinkingCompleteDmFailed(Logger, ex);
        }

        if (messageIdString != null)
        {
            var messageId = ulong.Parse(messageIdString);
            var message = await Context.Channel.GetMessageAsync(messageId).ConfigureAwait(false);
            await message.DeleteAsync().ConfigureAwait(false);
        }
    }

    private async Task HandleLinkingCanceledByAdminAsync(Result result, IUser discordUser, string? messageIdString)
    {
        try
        {
            if (result.IsFailure)
            {
                await FollowupAsync(FriendlyMessageFor(result.Error), ephemeral: true).ConfigureAwait(false);
                return;
            }

            await FollowupAsync("Account linking was successfully canceled.", ephemeral: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogAdminCompleteResponseFailed(Logger, ex);
        }

        try
        {
            var dmChannel = await discordUser.CreateDMChannelAsync().ConfigureAwait(false);
            await dmChannel.SendMessageAsync(
                $"Your GeoGuessr account linking process on the {Context.Guild.Name} server was canceled by an admin. You can now create a new linking request if you wish.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogLinkingCompleteDmFailed(Logger, ex);
        }

        if (messageIdString != null)
        {
            var messageId = ulong.Parse(messageIdString);
            var message = await Context.Channel.GetMessageAsync(messageId).ConfigureAwait(false);
            await message.DeleteAsync().ConfigureAwait(false);
        }
    }

    [LoggerMessage(LogLevel.Error,
        "Error while trying to open one time password input modal for linking request between discord user {DiscordUserId} on GeoGuessr account {GeoGuessrUserId}.")]
    static partial void LogOtpModalOpenFailed(ILogger logger, Exception ex, string discordUserId, string geoGuessrUserId);

    [LoggerMessage(LogLevel.Error,
        "Error while trying to open cancel modal for linking request between discord user {DiscordUserId} on GeoGuessr account {GeoGuessrUserId}.")]
    static partial void LogCancelModalOpenFailed(ILogger logger, Exception ex, string discordUserId, string geoGuessrUserId);

    [LoggerMessage(LogLevel.Warning, "Sending admin complete response failed.")]
    static partial void LogAdminCompleteResponseFailed(ILogger logger, Exception ex);

    [LoggerMessage(LogLevel.Warning, "Sending direct message to user with completed account linking failed.")]
    static partial void LogLinkingCompleteDmFailed(ILogger logger, Exception ex);
}
