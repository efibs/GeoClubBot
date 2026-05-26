using Constants;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Entities;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.UseCases.GeoGuessrAccountLinking;

namespace GeoClubBot.Discord.InputAdapters.Interactions;

[CommandContextType(InteractionContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.Administrator)]
[Group("gg-account-link-admin", "Commands for linking Discord accounts to GeoGuessr accounts")]
public class GeoGuessrAccountLinkAdminModule(
    ISender mediator,
    ILogger<GeoGuessrAccountLinkAdminModule> logger) : ClubBotInteractionModule(mediator, logger)
{
    [SlashCommand("complete", "Complete the account linking process")]
    public Task CompleteAccountLinkingProcessAsync(IUser discordUser, string geoGuessrUserId, string oneTimePassword) =>
        ExecuteAsync(
            async ct =>
            {
                var result = await Mediator
                    .Send(new CompleteAccountLinkingCommand(discordUser.Id, geoGuessrUserId, oneTimePassword), ct)
                    .ConfigureAwait(false);

                await _handleLinkingEndedAsync(result.Successful, result.User, discordUser, null).ConfigureAwait(false);
            },
            ephemeral: true,
            failureMessage: "Failed to complete linking process (internal error).");

    [SlashCommand("cancel", "Cancel the account linking process")]
    public Task CancelAccountLinkingProcessAsync(IUser discordUser, string geoGuessrUserId) =>
        ExecuteAsync(
            async ct =>
            {
                var result = await Mediator
                    .Send(new CancelAccountLinkingCommand(discordUser.Id, geoGuessrUserId), ct)
                    .ConfigureAwait(false);

                await _handleLinkingCanceledByAdminAsync(result, discordUser, null).ConfigureAwait(false);
            },
            ephemeral: true,
            failureMessage: "Failed to cancel linking process (internal error).");

    [SlashCommand("unlink", "Unlink the accounts of a user")]
    public Task UnlinkAccountsSlashCommandAsync(IUser discordUser, string geoGuessrUserId) =>
        ExecuteAsync(
            async ct =>
            {
                var successful = await Mediator
                    .Send(new UnlinkAccountsCommand(discordUser.Id, geoGuessrUserId), ct)
                    .ConfigureAwait(false);

                await FollowupAsync(
                        successful
                            ? "Account linking was successfully removed."
                            : "The given accounts are not linked",
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
            Logger.LogError(ex, "Error while trying to open one time password input modal for linking request between discord user {DiscordUserId} on GeoGuessr account {GeoGuessrUserId}.", discordUserIdString, geoGuessrUserId);
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
                await _handleLinkingEndedAsync(result.Successful, result.User, discordUser, messageIdString).ConfigureAwait(false);
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
            Logger.LogError(ex, "Error while trying to open cancel modal for linking request between discord user {DiscordUserId} on GeoGuessr account {GeoGuessrUserId}.", discordUserIdString, geoGuessrUserId);
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
                await _handleLinkingCanceledByAdminAsync(result, discordUser, messageIdString).ConfigureAwait(false);
            },
            ephemeral: true,
            failureMessage: "Failed to cancel linking process (internal error).");

    private async Task _handleLinkingEndedAsync(bool successful, GeoGuessrUser? geoGuessrUser, IUser discordUser, string? messageIdString)
    {
        try
        {
            if (!successful)
            {
                await FollowupAsync("Account linking failed: Wrong password. Please try again.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            await FollowupAsync("Account linking was successful.", ephemeral: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Sending admin complete response failed.");
        }

        try
        {
            var dmChannel = await discordUser.CreateDMChannelAsync().ConfigureAwait(false);
            await dmChannel.SendMessageAsync(
                $"Your GeoGuessr account \"{geoGuessrUser?.Nickname ?? "N/A"}\" was successfully linked to this Discord account.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Sending direct message to user with completed account linking failed.");
        }

        if (messageIdString != null)
        {
            var messageId = ulong.Parse(messageIdString);
            var message = await Context.Channel.GetMessageAsync(messageId).ConfigureAwait(false);
            await message.DeleteAsync().ConfigureAwait(false);
        }
    }

    private async Task _handleLinkingCanceledByAdminAsync(bool successful, IUser discordUser, string? messageIdString)
    {
        try
        {
            if (!successful)
            {
                await FollowupAsync("There was no account linking process for the given accounts.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            await FollowupAsync("Account linking was successfully canceled.", ephemeral: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Sending admin complete response failed.");
        }

        try
        {
            var dmChannel = await discordUser.CreateDMChannelAsync().ConfigureAwait(false);
            await dmChannel.SendMessageAsync(
                $"Your GeoGuessr account linking process on the {Context.Guild.Name} server was canceled by an admin. You can now create a new linking request if you wish.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Sending direct message to user with completed account linking failed.");
        }

        if (messageIdString != null)
        {
            var messageId = ulong.Parse(messageIdString);
            var message = await Context.Channel.GetMessageAsync(messageId).ConfigureAwait(false);
            await message.DeleteAsync().ConfigureAwait(false);
        }
    }
}
