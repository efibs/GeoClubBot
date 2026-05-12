using Configuration;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Infrastructure.InputAdapters;

public class UserLeftService(DiscordSocketClient client, IOptions<DiscordConfiguration> config) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        client.UserLeft += _onUserLeftAsync;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        client.UserLeft -= _onUserLeftAsync;
        return Task.CompletedTask;
    }

    private async Task _onUserLeftAsync(SocketGuild guild, SocketUser user)
    {
        if (guild.Id != config.Value.ServerId)
        {
            return;
        }

        var channel = guild.GetTextChannel(config.Value.LeftTextChannelId);
        var messageContent = config.Value.LeftMessage.Replace("{{User}}", user.Mention);
        await channel.SendMessageAsync(messageContent).ConfigureAwait(false);
    }
}
