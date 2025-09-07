using System.Text.RegularExpressions;
using Constants;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.GeoGuessrAccountLinking;

namespace Infrastructure.InputAdapters.Interactions;

[CommandContextType(InteractionContextType.Guild)]
[Group("gg-account-link", "Commands for linking Discord accounts to GeoGuessr accounts")]
public class GeoGuessrAccountLinkModule(IGetLinkedDiscordUserIdUseCase getLinkedDiscordUserIdUseCase, 
    IStartAccountLinkingProcessUseCase startAccountLinkingProcessUseCase, 
    ICompleteAccountLinkingUseCase completeAccountLinkingUseCase,
    IUnlinkAccountsUseCase unlinkAccountsUseCase,
    ILogger<GeoGuessrAccountLinkModule> logger,
    IConfiguration config) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("link", "Link your Discord account to a GeoGuessr account")]
    public async Task StartLinkingProcessAsync([Summary(description: "The link to your profile")] string shareProfileLink)
    {
        try
        {
            // Get the executing user
            var executingUser = Context.User as SocketGuildUser;
            
            // Try to get the user id
            var parseSuccessful = _tryParseUserIdFromProfileLink(shareProfileLink, out var geoGuessrUserId);

            // If the link was in the wrong format
            if (parseSuccessful == false || geoGuessrUserId == null)
            {
                // Respond with error message
                await RespondAsync("Account link failed: The profile link was in the wrong format.\n\n" +
                                   "The link should look something like this: https://www.geoguessr.com/user/62c353a29d0d57e7b9a3383f. " +
                                   "You can retrieve the link from your profile page: In the top right of GeoGuessr go to \"Profile\". " +
                                   "Then scroll all the way down. There you can copy your profile link.\n\nPlease try again.",
                    ephemeral: true);

                return;
            }

            // Check if the GeoGuessr account is already linked
            var linkedDiscordUserId =
                await getLinkedDiscordUserIdUseCase.GetLinkedDiscordUserIdAsync(geoGuessrUserId);
            
            // If the account is already linked
            if (linkedDiscordUserId.HasValue)
            {
                // Get the linked discord user
                var linkedDiscordUser = Context.Guild.GetUser(linkedDiscordUserId.Value);
                
                // Respond with error message
                await RespondAsync("Account link failed: The GeoGuessr account is already linked to discord account " +
                                   $"\"{linkedDiscordUser.DisplayName}\". Please contact an admin if you think this is a mistake.",
                    ephemeral: true);

                return;
            }
            
            // Start the account linking process
            var oneTimePassword = await startAccountLinkingProcessUseCase.StartLinkingProcessAsync(executingUser!.Id, geoGuessrUserId);

            // If there was already a linking request
            if (oneTimePassword == null)
            {
                // Respond with error message
                await RespondAsync("Account link failed: Linking process already started. " +
                                   "If you have not received a one time password, then please contact an admin. " +
                                   "If you have already sent the one time password to an admin in GeoGuessr, lean back " +
                                   "and relax while the admins are processing your request.",
                    ephemeral: true);

                return;
            }
            
            // Respond with further instructions
            await RespondAsync("Account linking process successfully started. To complete the linking process, " +
                               $"please send this one time password to an admin in GeoGuessr: {oneTimePassword}\n\n" +
                               "The admin will then complete your request and you will get notified.", ephemeral: true);
            
            // Send admin message
            await _sendAdminAccountLinkingStartedMessageAsync(executingUser, geoGuessrUserId);
        }
        catch (Exception ex)
        {
            // Log error
            logger.LogError(ex, $"Account linking process failed for profile: {shareProfileLink}.");
            
            // Respond with error message
            await RespondAsync("Account link failed: Internal error. Try again later. If the problem persists, please contact an admin.", ephemeral: true);
        }
    }
    
    [SlashCommand("complete", "Complete the account linking process")]
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    public async Task CompleteAccountLinkingProcessAsync(IUser discordUser, string geoGuessrUserId, string oneTimePassword)
    {
        // Complete the account linking
        var result =
            await completeAccountLinkingUseCase.CompleteLinkingAsync(discordUser.Id, geoGuessrUserId, oneTimePassword);
        
        // Handle the linking ended
        await _handleLinkingEndedAsync(result.Successful, result.User, discordUser, null);
    }
    
    [SlashCommand("unlink", "Unlink the accounts of a user")]
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    public async Task UnlinkAccountsSlashCommandAsync(IUser discordUser, string geoGuessrUserId)
    {
        // Unlink the accounts
        var successful = await unlinkAccountsUseCase.UnlinkAccountsAsync(discordUser.Id, geoGuessrUserId);
        
        // If the unlink was not successful
        if (successful == false)
        {
            // Respond with error
            await RespondAsync("The given accounts are not linked", ephemeral: true);
            return;
        }
        
        // Respond with successful message
        await RespondAsync("Account linking was successfully removed.", ephemeral: true);
    }

    [ComponentInteraction($"{ComponentIds.GeoGuessrAccountLinkingCompleteButtonId}:*,*", true)]
    public async Task CompleteLinkingButtonPressedAsync(string discordUserIdString, string geoGuessrUserId)
    {
        // Get the message id
        var messageId = (Context.Interaction as SocketMessageComponent)?.Message?.Id;
        
        // Build the modal id
        var modalId = $"{ComponentIds.GeoGuessrAccountLinkingCompleteModalId}:{discordUserIdString},{geoGuessrUserId},{messageId}";
        
        // Send the modal
        await Context.Interaction.RespondWithModalAsync<CompleteAccountLinkModal>(modalId);
    }

    [ModalInteraction($"{ComponentIds.GeoGuessrAccountLinkingCompleteModalId}:*,*,*", true)]
    public async Task CompleteLinkingPasswordSubmitted(string discordUserIdString, string geoGuessrUserId, string messageIdString, CompleteAccountLinkModal modal)
    {
        try
        {
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
            await RespondAsync("Failed to complete linking process.", ephemeral: true);
        }
    }
    
    private bool _tryParseUserIdFromProfileLink(string profileLink, out string? userId)
    {
        // Initialize user id to null
        userId = null;
        
        // Check if the link is in the correct format
        if (ShareProfileLinkCheckerRegex.IsMatch(profileLink) == false)
        {
            // Link is not in the correct format
            return false;
        }
        
        // Find the index of the last slash
        var splitIndex = profileLink.LastIndexOf('/');
        
        // Get the user id from the url
        userId = profileLink.Substring(splitIndex + 1);

        return true;
    }

    private async Task _sendAdminAccountLinkingStartedMessageAsync(SocketGuildUser executingUser, string geoGuessrUserId)
    {
        // Get admin text channel
        var adminTextChannel = Context.Guild.GetTextChannel(_accountLinkingAdminChannelId);

        // Build the id for the complete button
        var completeButtonId = $"{ComponentIds.GeoGuessrAccountLinkingCompleteButtonId}:{executingUser.Id},{geoGuessrUserId}";
        
        // Build the send complete modal button
        var completeButton = new ComponentBuilder()
            .WithButton("Complete", completeButtonId)
            .Build();
        
            // Send message to the admins
            await adminTextChannel
            .SendMessageAsync($"User {executingUser.DisplayName} (id: {executingUser.Id}) " +
                              $"started the linking process for GeoGuessr account {geoGuessrUserId}. " +
                              $"Click the button below to complete the linking process.\n\n" +
                              $"**Only accept the password if it was sent to you by the correct user inside GeoGuessr!**", 
                components: completeButton);
    }

    private async Task _handleLinkingEndedAsync(bool successful, GeoGuessrUser? geoGuessrUser, IUser discordUser, string? messageIdString)
    {
        try
        {
            // If the linking was not successful
            if (successful == false)
            {
                // Respond with wrong password message
                await RespondAsync("Account linking failed: Wrong password. Please try again.", ephemeral: true);
                return;
            }

            // Respond with successful message
            await RespondAsync("Account linking was successful.", ephemeral: true);
        }
        catch
        {
            // ignored
        }

        try
        {
            // Create a direct message channel with the user
            var dmChannel = await discordUser.CreateDMChannelAsync();
            
            // Send successful message
            await dmChannel.SendMessageAsync(
                $"Your GeoGuessr account \"{geoGuessrUser?.Nickname ?? "N/A"}\" was successfully linked to this Discord account.");
        }
        catch
        {
            // ignored
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
    
    private static readonly Regex ShareProfileLinkCheckerRegex =
        new Regex(@"^https:\/\/www\.geoguessr\.com\/user\/[\da-z]{24}$", RegexOptions.Compiled);

    private readonly ulong _accountLinkingAdminChannelId =
        config.GetValue<ulong>(ConfigKeys.GeoGuessrAccountLinkingAdminChannelIdConfigurationKey);
}