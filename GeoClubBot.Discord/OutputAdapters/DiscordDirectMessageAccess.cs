using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using UseCases.OutputPorts.Discord;

namespace GeoClubBot.Discord.OutputAdapters;

public partial class DiscordDirectMessageAccess(DiscordSocketClient client, ILogger<DiscordDirectMessageAccess> logger)
    : IDiscordDirectMessageAccess
{
    public async Task<bool> SendDirectMessageAsync(ulong discordUserId, string message)
    {
        try
        {
            var user = await client.GetUserAsync(discordUserId).ConfigureAwait(false);

            if (user == null)
            {
                LogUserNotFound(discordUserId);
                return false;
            }

            var dmChannel = await user.CreateDMChannelAsync().ConfigureAwait(false);
            await dmChannel.SendMessageAsync(message).ConfigureAwait(false);

            return true;
        }
        catch (Exception ex)
        {
            LogFailedToSendDm(discordUserId, ex);
            return false;
        }
    }

    [LoggerMessage(LogLevel.Warning, "Failed to send DM: user {DiscordUserId} not found.")]
    partial void LogUserNotFound(ulong discordUserId);

    [LoggerMessage(LogLevel.Warning, "Failed to send DM to user {DiscordUserId}.")]
    partial void LogFailedToSendDm(ulong discordUserId, Exception ex);
}
