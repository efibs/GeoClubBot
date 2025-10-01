using Constants;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Entities;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.GeoGuessrAccountLinking;

namespace Infrastructure.InputAdapters.Interactions;

[CommandContextType(InteractionContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.Administrator)]
[Group("gg-account-link-admin", "Commands for linking Discord accounts to GeoGuessr accounts")]
public class GeoGuessrAccountLinkAdminModule(ICompleteAccountLinkingUseCase completeAccountLinkingUseCase,
    IUnlinkAccountsUseCase unlinkAccountsUseCase,
    ICancelAccountLinkingUseCase cancelAccountLinkingUseCase,
    ILogger<GeoGuessrAccountLinkAdminModule> logger) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("complete", "Complete the account linking process")]
    public async Task CompleteAccountLinkingProcessAsync(IUser discordUser, string geoGuessrUserId, string oneTimePassword)
    {
        try
        {
            // Defer the response
            await DeferAsync(ephemeral: true).ConfigureAwait(false);

            // Complete the account linking
            var result =
                await completeAccountLinkingUseCase.CompleteLinkingAsync(discordUser.Id, geoGuessrUserId,
                    oneTimePassword).ConfigureAwait(false);

            // Handle the linking ended
            await _handleLinkingEndedAsync(result.Successful, result.User, discordUser, null).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Log error
            logger.LogError(ex, $"Slash command complete account linking request failed for discord user {discordUser.Username} ({discordUser.Id}) on GeoGuessr account {geoGuessrUserId}.");
            
            // Respond
            await  FollowupAsync("Failed to complete linking process.", ephemeral: true).ConfigureAwait(false);
        }
    }
    
    [SlashCommand("cancel", "Cancel the account linking process")]
    public async Task CancelAccountLinkingProcessAsync(IUser discordUser, string geoGuessrUserId)
    {
        try
        {
            // Defer the response
            await DeferAsync(ephemeral: true).ConfigureAwait(false);

            // Complete the account linking
            var result =
                await cancelAccountLinkingUseCase.CancelAccountLinkingAsync(discordUser.Id, geoGuessrUserId).ConfigureAwait(false);

            // Handle the linking ended
            await _handleLinkingCanceledByAdminAsync(result, discordUser, null).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Log error
            logger.LogError(ex, $"Slash command complete account linking request failed for discord user {discordUser.Username} ({discordUser.Id}) on GeoGuessr account {geoGuessrUserId}.");
            
            // Respond
            await  FollowupAsync("Failed to complete linking process.", ephemeral: true).ConfigureAwait(false);
        }
    }
    
    [SlashCommand("unlink", "Unlink the accounts of a user")]
    public async Task UnlinkAccountsSlashCommandAsync(IUser discordUser, string geoGuessrUserId)
    {
        try
        {
            // Defer the response
            await DeferAsync(ephemeral: true).ConfigureAwait(false);
        
            // Unlink the accounts
            var successful = await unlinkAccountsUseCase.UnlinkAccountsAsync(discordUser.Id, geoGuessrUserId).ConfigureAwait(false);
        
            // If the unlink was not successful
            if (successful == false)
            {
                // Respond with error
                await FollowupAsync("The given accounts are not linked", ephemeral: true).ConfigureAwait(false);
                return;
            }
        
            // Respond with successful message
            await FollowupAsync("Account linking was successfully removed.", ephemeral: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Log error
            logger.LogError(ex, $"Slash command unlink accounts failed for discord user {discordUser.Username} ({discordUser.Id}) on GeoGuessr account {geoGuessrUserId}.");
            
            // Respond
            await  FollowupAsync("Failed to remove account link.", ephemeral: true).ConfigureAwait(false);
        }

    }

    [ComponentInteraction($"{ComponentIds.GeoGuessrAccountLinkingCompleteButtonId}:*,*", true)]
    public async Task CompleteLinkingButtonPressedAsync(string discordUserIdString, string geoGuessrUserId)
    {
        try
        {
            // Get the message id
            var messageId = (Context.Interaction as SocketMessageComponent)?.Message?.Id;

            // Build the modal id
            var modalId =
                $"{ComponentIds.GeoGuessrAccountLinkingCompleteModalId}:{discordUserIdString},{geoGuessrUserId},{messageId}";

            // Send the modal
            await Context.Interaction.RespondWithModalAsync<CompleteAccountLinkModal>(modalId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Log error
            logger.LogError(ex, $"Error while trying to open one time password input modal for linking request between discord user {discordUserIdString} on GeoGuessr account {geoGuessrUserId}.");
            
            // Respond
            await RespondAsync("Failed to open one time password input modal.", ephemeral: true).ConfigureAwait(false);
        }
    }

    [ModalInteraction($"{ComponentIds.GeoGuessrAccountLinkingCompleteModalId}:*,*,*", true)]
    public async Task CompleteLinkingPasswordSubmitted(string discordUserIdString, string geoGuessrUserId, string messageIdString, CompleteAccountLinkModal modal)
    {
        try
        {
            // Defer the response
            await DeferAsync(ephemeral: true).ConfigureAwait(false);
            
            // Parse the discord user id
            var discordUserId = ulong.Parse(discordUserIdString);

            // Complete the request
            var result = await completeAccountLinkingUseCase
                .CompleteLinkingAsync(discordUserId, geoGuessrUserId, modal.OneTimePassword).ConfigureAwait(false);

            // Get the discord user
            var discordUser = Context.Guild.GetUser(discordUserId);
            
            // Handle the linking ended
            await _handleLinkingEndedAsync(result.Successful, result.User, discordUser, messageIdString).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Log error
            logger.LogError(ex, $"Failed to link GeoGuessr Account '{geoGuessrUserId}' to Discord user '{discordUserIdString}'.");
            
            // Respond with error
            await FollowupAsync("Failed to complete linking process.", ephemeral: true).ConfigureAwait(false);
        }
    }
    
    [ComponentInteraction($"{ComponentIds.GeoGuessrAccountLinkingCancelButtonId}:*,*", true)]
    public async Task CancelLinkingButtonPressedAsync(string discordUserIdString, string geoGuessrUserId)
    {
        try
        {
            // Get the message id
            var messageId = (Context.Interaction as SocketMessageComponent)?.Message?.Id;

            // Build the modal id
            var modalId =
                $"{ComponentIds.GeoGuessrAccountLinkingCancelModalId}:{discordUserIdString},{geoGuessrUserId},{messageId}";

            // Send the modal
            await Context.Interaction.RespondWithModalAsync<CancelAccountLinkModal>(modalId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Log error
            logger.LogError(ex, $"Error while trying to open cancel modal for linking request between discord user {discordUserIdString} on GeoGuessr account {geoGuessrUserId}.");
            
            // Respond
            await RespondAsync("Failed to open cancel modal.", ephemeral: true).ConfigureAwait(false);
        }
    }
    
    [ModalInteraction($"{ComponentIds.GeoGuessrAccountLinkingCancelModalId}:*,*,*", true)]
    public async Task CancelLinkingSubmitted(string discordUserIdString, string geoGuessrUserId, string messageIdString, CancelAccountLinkModal modal)
    {
        try
        {
            // Defer the response
            await DeferAsync(ephemeral: true).ConfigureAwait(false);
            
            // If the user did not enter "Confirm"
            if (modal.ConfirmText != "Confirm")
            {
                // Respond with successful message
                await FollowupAsync("Account linking was not canceled. To cancel please enter 'Confirm' into the Confirm Text box.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            // Parse the discord user id
            var discordUserId = ulong.Parse(discordUserIdString);

            // Complete the request
            var result = await cancelAccountLinkingUseCase
                .CancelAccountLinkingAsync(discordUserId, geoGuessrUserId).ConfigureAwait(false);

            // Get the discord user
            var discordUser = Context.Guild.GetUser(discordUserId);
            
            // Handle the linking ended
            await _handleLinkingCanceledByAdminAsync(result, discordUser, messageIdString).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Log error
            logger.LogError(ex, $"Failed to link GeoGuessr Account '{geoGuessrUserId}' to Discord user '{discordUserIdString}'.");
            
            // Respond with error
            await FollowupAsync("Failed to complete linking process.", ephemeral: true).ConfigureAwait(false);
        }
    }
    
    private async Task _handleLinkingEndedAsync(bool successful, GeoGuessrUser? geoGuessrUser, IUser discordUser, string? messageIdString)
    {
        try
        {
            // If the linking was not successful
            if (successful == false)
            {
                // Respond with wrong password message
                await FollowupAsync("Account linking failed: Wrong password. Please try again.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            // Respond with successful message
            await FollowupAsync("Account linking was successful.", ephemeral: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // log warning
            logger.LogWarning(ex, "Sending admin complete response failed.");
        }

        try
        {
            // Create a direct message channel with the user
            var dmChannel = await discordUser.CreateDMChannelAsync().ConfigureAwait(false);
            
            // Send successful message
            await dmChannel.SendMessageAsync(
                $"Your GeoGuessr account \"{geoGuessrUser?.Nickname ?? "N/A"}\" was successfully linked to this Discord account.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // log warning
            logger.LogWarning(ex, "Sending direct message to user with completed account linking failed.");
        }

        // If the original message id is still existing
        if (messageIdString != null)
        {
            // Parse the message id
            var messageId = ulong.Parse(messageIdString);
            
            // Get the original message
            var message = await Context.Channel.GetMessageAsync(messageId).ConfigureAwait(false);
            
            // Delete the original message
            await message.DeleteAsync().ConfigureAwait(false);
        }
    }
    
    private async Task _handleLinkingCanceledByAdminAsync(bool successful, IUser discordUser, string? messageIdString)
    {
        try
        {
            // If the cancel was not successful
            if (successful == false)
            {
                // Respond with wrong password message
                await FollowupAsync("There was no account linking process for the given accounts.", ephemeral: true).ConfigureAwait(false);
                return;
            }
            
            // Respond with successful message
            await FollowupAsync("Account linking was successfully canceled.", ephemeral: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // log warning
            logger.LogWarning(ex, "Sending admin complete response failed.");
        }

        try
        {
            // Create a direct message channel with the user
            var dmChannel = await discordUser.CreateDMChannelAsync().ConfigureAwait(false);
            
            // Send successful message
            await dmChannel.SendMessageAsync(
                $"Your GeoGuessr account linking process on the {Context.Guild.Name} server was canceled by an admin. You can now create a new linking request if you wish.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // log warning
            logger.LogWarning(ex, "Sending direct message to user with completed account linking failed.");
        }

        // If the original message id is still existing
        if (messageIdString != null)
        {
            // Parse the message id
            var messageId = ulong.Parse(messageIdString);
            
            // Get the original message
            var message = await Context.Channel.GetMessageAsync(messageId).ConfigureAwait(false);
            
            // Delete the original message
            await message.DeleteAsync().ConfigureAwait(false);
        }
    }
}