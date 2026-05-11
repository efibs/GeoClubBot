using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.GeoGuessrAccountLinking;

namespace GeoClubBot.Discord.InputAdapters.Interactions;

[CommandContextType(InteractionContextType.Guild)]
[Group("user-info", "Commands for getting information about a user")]
public partial class UserInfoModule(IGetLinkedGeoGuessrUserUseCase getLinkedGeoGuessrUserUseCase,
    IGetDiscordUserByNicknameUseCase getDiscordUserByNicknameUseCase,
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

    [SlashCommand("discord-user", "Get the Discord user for a GeoGuessr nickname")]
    public async Task GetDiscordUserAsync(string nickname)
    {
        try
        {
            await DeferAsync(ephemeral: true).ConfigureAwait(false);

            var discordUserId = await getDiscordUserByNicknameUseCase
                .GetDiscordUserIdByNicknameAsync(nickname)
                .ConfigureAwait(false);

            if (discordUserId == null)
            {
                await FollowupAsync($"No linked Discord user found for GeoGuessr player '**{nickname}**'.", ephemeral: true)
                    .ConfigureAwait(false);
            }
            else
            {
                await FollowupAsync($"The GeoGuessr player '**{nickname}**' is <@{discordUserId}>.", ephemeral: true)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            LogReadingTheDiscordUserOfNicknameFailed(logger, ex, nickname);
            await FollowupAsync("Reading the Discord user failed. Try again later. If the problem persists, please contact an admin.", ephemeral: true).ConfigureAwait(false);
        }
    }

    [LoggerMessage(LogLevel.Error, "Reading the Discord user for GeoGuessr nickname '{nickname}' failed.")]
    static partial void LogReadingTheDiscordUserOfNicknameFailed(ILogger<UserInfoModule> logger, Exception ex, string nickname);
}