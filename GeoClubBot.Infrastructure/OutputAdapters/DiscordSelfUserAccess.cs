using Discord.WebSocket;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class DiscordSelfUserAccess(DiscordSocketClient client) : ISelfUserAccess
{
    public ulong GetSelfUserId()
    {
        return client.CurrentUser.Id;
    }
}