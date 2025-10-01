using System.Text.RegularExpressions;
using Constants;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.GeoGuessrAccountLinking;

namespace Infrastructure.InputAdapters.Interactions;

[CommandContextType(InteractionContextType.Guild)]
[Group("gg-account", "Commands for linking Discord accounts to GeoGuessr accounts")]
public class GeoGuessrAccountLinkPublicModule(IGetLinkedDiscordUserIdUseCase getLinkedDiscordUserIdUseCase, 
    IStartAccountLinkingProcessUseCase startAccountLinkingProcessUseCase, 
    ILogger<GeoGuessrAccountLinkPublicModule> logger,
    IConfiguration config) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("link", "Link your Discord account to a GeoGuessr account")]
    public async Task StartLinkingProcessAsync([Summary(description: "The link to your profile")] string shareProfileLink)
    {
        try
        {
            // Defer the response
            await DeferAsync(ephemeral: true);
            
            // Get the executing user
            var executingUser = Context.User as SocketGuildUser;
            
            // Try to get the user id
            var parseSuccessful = _tryParseUserIdFromProfileLink(shareProfileLink, out var geoGuessrUserId);

            // If the link was in the wrong format
            if (parseSuccessful == false || geoGuessrUserId == null)
            {
                // Respond with error message
                await FollowupAsync("Account link failed: The profile link was in the wrong format.\n\n" +
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
                await FollowupAsync("Account link failed: The GeoGuessr account is already linked to discord account " +
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
                await FollowupAsync("Account link failed: Linking process already started. " +
                                    "If you have not received a one time password, then please contact an admin. " +
                                    "If you have already sent the one time password to an admin in GeoGuessr, lean back " +
                                    "and relax while the admins are processing your request.",
                    ephemeral: true);

                return;
            }
            
            // Respond with further instructions
            await FollowupAsync("**IMPORTANT! READ CAREFULLY!**: Account linking process successfully started. To complete the linking process, " +
                                "please send the following one time password to an admin of this Discord server as a direct message **in GeoGuessr**. Do not send " +
                                "the password here in Discord or anywhere else other than as a direct message in GeoGuessr.\n" +
                                $"Here is your one time password: {oneTimePassword}\n\n" +
                                "The admin will then complete your request and you will get notified.", ephemeral: true);
            
            // Send admin message
            await _sendAdminAccountLinkingStartedMessageAsync(executingUser, geoGuessrUserId);
        }
        catch (Exception ex)
        {
            // Log error
            logger.LogError(ex, $"Account linking process failed for profile: {shareProfileLink}.");
            
            // Respond with error message
            await FollowupAsync("Account link failed: Internal error. Try again later. If the problem persists, please contact an admin.", ephemeral: true);
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
    
    private static readonly Regex ShareProfileLinkCheckerRegex =
        new(@"^https:\/\/www\.geoguessr\.com\/user\/[\da-z]{24}$", RegexOptions.Compiled);

    private readonly ulong _accountLinkingAdminChannelId =
        config.GetValue<ulong>(ConfigKeys.GeoGuessrAccountLinkingAdminChannelIdConfigurationKey);
}