using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using UseCases.OutputPorts.Discord;
using Utilities;

namespace GeoClubBot.Discord.OutputAdapters;

public partial class DiscordDirectMessageAccess(DiscordSocketClient client, ILogger<DiscordDirectMessageAccess> logger)
    : IDiscordDirectMessageAccess
{
    public async Task<Result> SendDirectMessageAsync(ulong discordUserId, string message, CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await client.GetUserAsync(discordUserId).ConfigureAwait(false);

            if (user == null)
            {
                // Could be a cache miss / replication lag, so treat as transient rather than a privacy block.
                LogUserNotFound(discordUserId);
                return Error.Unexpected("discord.dm.failed", "Could not resolve the Discord user to send a direct message to.");
            }

            var dmChannel = await user.CreateDMChannelAsync().ConfigureAwait(false);
            await dmChannel.SendMessageAsync(message).ConfigureAwait(false);

            return Result.Success();
        }
        catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.CannotSendMessageToUser)
        {
            // 50007: the recipient does not accept DMs from this bot (privacy settings) or has blocked it.
            // This is permanent until the user changes their settings — distinct from a transient failure.
            LogDmsDisabled(discordUserId);
            return Error.Forbidden(DiscordDmErrorCodes.Disabled, "The user does not accept direct messages from the bot.");
        }
        catch (HttpException ex) when ((int?)ex.DiscordCode == 50278)
        {
            // 50278: no mutual guilds — the user has left the server, so the bot can never DM them.
            // Discord.Net has no named DiscordErrorCode member for this code, hence the numeric match.
            LogNoMutualGuild(discordUserId);
            return Error.NotFound(DiscordDmErrorCodes.NoMutualGuild, "Cannot DM the user; they no longer share a guild with the bot.");
        }
        catch (Exception ex)
        {
            // Network blips, rate limits, Discord outages, etc. — may succeed on a later attempt.
            LogFailedToSendDm(discordUserId, ex);
            return Error.Unexpected("discord.dm.failed", "Failed to send the direct message due to an unexpected error.");
        }
    }

    [LoggerMessage(LogLevel.Warning, "Failed to send DM: user {DiscordUserId} not found.")]
    partial void LogUserNotFound(ulong discordUserId);

    [LoggerMessage(LogLevel.Warning, "Could not send DM to user {DiscordUserId}: the user has DMs from the bot disabled or has blocked the bot.")]
    partial void LogDmsDisabled(ulong discordUserId);

    [LoggerMessage(LogLevel.Warning, "Could not send DM to user {DiscordUserId}: no mutual guild — the user has left the server.")]
    partial void LogNoMutualGuild(ulong discordUserId);

    [LoggerMessage(LogLevel.Warning, "Failed to send DM to user {DiscordUserId} due to an unexpected error.")]
    partial void LogFailedToSendDm(ulong discordUserId, Exception ex);
}
