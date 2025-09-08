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
    ILogger<GeoGuessrAccountLinkAdminModule> logger) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("complete", "Complete the account linking process")]
    public async Task CompleteAccountLinkingProcessAsync(IUser discordUser, string geoGuessrUserId, string oneTimePassword)
    {
        try
        {
            // Defer the response
            await DeferAsync(ephemeral: true);

            // Complete the account linking
            var result =
                await completeAccountLinkingUseCase.CompleteLinkingAsync(discordUser.Id, geoGuessrUserId,
                    oneTimePassword);

            // Handle the linking ended
            await _handleLinkingEndedAsync(result.Successful, result.User, discordUser, null);
        }
        catch (Exception ex)
        {
            // Log error
            logger.LogError(ex, $"Slash command complete account linking request failed for discord user {discordUser.Username} ({discordUser.Id}) on GeoGuessr account {geoGuessrUserId}.");
            
            // Respond
            await  FollowupAsync("Failed to complete linking process.", ephemeral: true);
        }
    }
    
    [SlashCommand("unlink", "Unlink the accounts of a user")]
    public async Task UnlinkAccountsSlashCommandAsync(IUser discordUser, string geoGuessrUserId)
    {
        try
        {
            // Defer the response
            await DeferAsync(ephemeral: true);
        
            // Unlink the accounts
            var successful = await unlinkAccountsUseCase.UnlinkAccountsAsync(discordUser.Id, geoGuessrUserId);
        
            // If the unlink was not successful
            if (successful == false)
            {
                // Respond with error
                await FollowupAsync("The given accounts are not linked", ephemeral: true);
                return;
            }
        
            // Respond with successful message
            await FollowupAsync("Account linking was successfully removed.", ephemeral: true);
        }
        catch (Exception ex)
        {
            // Log error
            logger.LogError(ex, $"Slash command unlink accounts failed for discord user {discordUser.Username} ({discordUser.Id}) on GeoGuessr account {geoGuessrUserId}.");
            
            // Respond
            await  FollowupAsync("Failed to remove account link.", ephemeral: true);
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
            await Context.Interaction.RespondWithModalAsync<CompleteAccountLinkModal>(modalId);
        }
        catch (Exception ex)
        {
            // Log error
            logger.LogError(ex, $"Error while trying to open one time password input modal for linking request between discord user {discordUserIdString} on GeoGuessr account {geoGuessrUserId}.");
            
            // Respond
            await RespondAsync("Failed to open one time password input modal.", ephemeral: true);
        }
    }

    [ModalInteraction($"{ComponentIds.GeoGuessrAccountLinkingCompleteModalId}:*,*,*", true)]
    public async Task CompleteLinkingPasswordSubmitted(string discordUserIdString, string geoGuessrUserId, string messageIdString, CompleteAccountLinkModal modal)
    {
        try
        {
            // Defer the response
            await DeferAsync(ephemeral: true);
            
            // Parse the discord user id
            var discordUserId = ulong.Parse(discordUserIdString);

            // Complete the request
            var result = await completeAccountLinkingUseCase
                .CompleteLinkingAsync(discordUserId, geoGuessrUserId, modal.OneTimePassword);

            // Get the discord user
            var discordUser = Context.Guild.GetUser(discordUserId);
            
            // Handle the linking ended
            await _handleLinkingEndedAsync(result.Successful, result.User, discordUser, messageIdString);
        }
        catch (Exception ex)
        {
            // Log error
            logger.LogError(ex, $"Failed to link GeoGuessr Account '{geoGuessrUserId}' to Discord user '{discordUserIdString}'.");
            
            // Respond with error
            await FollowupAsync("Failed to complete linking process.", ephemeral: true);
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
                await FollowupAsync("Account linking failed: Wrong password. Please try again.", ephemeral: true);
                return;
            }

            // Respond with successful message
            await FollowupAsync("Account linking was successful.", ephemeral: true);
        }
        catch (Exception ex)
        {
            // log warning
            logger.LogWarning(ex, "Sending admin complete response failed.");
        }

        try
        {
            // Create a direct message channel with the user
            var dmChannel = await discordUser.CreateDMChannelAsync();
            
            // Send successful message
            await dmChannel.SendMessageAsync(
                $"Your GeoGuessr account \"{geoGuessrUser?.Nickname ?? "N/A"}\" was successfully linked to this Discord account.");
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
            var message = await Context.Channel.GetMessageAsync(messageId);
            
            // Delete the original message
            await message.DeleteAsync();
        }
    }
}