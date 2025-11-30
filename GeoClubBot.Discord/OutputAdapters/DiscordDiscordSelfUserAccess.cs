using Discord.WebSocket;
using UseCases.OutputPorts.Discord;

namespace GeoClubBot.Discord.OutputAdapters;

public class DiscordDiscordSelfUserAccess(DiscordSocketClient client) : IDiscordSelfUserAccess
{
    public ulong GetSelfUserId()
    {
        return client.CurrentUser.Id;
    }
}