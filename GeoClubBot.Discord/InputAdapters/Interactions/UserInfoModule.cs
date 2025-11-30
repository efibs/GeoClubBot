using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.GeoGuessrAccountLinking;

namespace GeoClubBot.Discord.InputAdapters.Interactions;

[CommandContextType(InteractionContextType.Guild)]
[Group("user-info", "Commands for getting information about a user")]
public partial class UserInfoModule(IGetLinkedGeoGuessrUserUseCase getLinkedGeoGuessrUserUseCase,
    ILogger<UserInfoModule> logger) 
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("gg-nickname", "Get the GeoGuessr nickname of a user")]
    [UserCommand("gg-nickname")]
    public async Task GetGeoGuessrNicknameAsync(IGuildUser user)
    {
        try
        {
            // Defer the response
            await DeferAsync(ephemeral: true).ConfigureAwait(false);
            
            // Get the linked account
            var linkedGeoGuessrAccount = await getLinkedGeoGuessrUserUseCase
                .GetLinkedGeoGuessrUserAsync(user.Id)
                .ConfigureAwait(false);

            // If the user is not linked
            if (linkedGeoGuessrAccount == null)
            {
                // Send response
                await FollowupAsync($"The user '{user.DisplayName}' has not linked his GeoGuessr account yet.", ephemeral: true)
                    .ConfigureAwait(false);
            }
            else
            {
                // Send response
                await FollowupAsync($"The user '{user.DisplayName}' is called '**{linkedGeoGuessrAccount.Nickname}**' in GeoGuessr.", ephemeral: true)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // Log error
            LogReadingTheGeoguessrNicknameOfUserFailed(logger, ex, user.DisplayName);
            
            // Respond with error message
            await FollowupAsync("Reading the GeoGuessr nickname failed. Try again later. If the problem persists, please contact an admin.", ephemeral: true).ConfigureAwait(false);
        }
    }

    [LoggerMessage(LogLevel.Error, "Reading the GeoGuessr nickname of the user '{userDisplayName}' failed.")]
    static partial void LogReadingTheGeoguessrNicknameOfUserFailed(ILogger<UserInfoModule> logger, Exception ex, string userDisplayName);
}