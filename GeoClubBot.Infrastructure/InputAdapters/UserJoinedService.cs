using Constants;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Infrastructure.InputAdapters;

public class UserJoinedService(DiscordSocketClient client, IConfiguration config) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        client.UserJoined += _onUserJoinedAsync;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        client.UserJoined -= _onUserJoinedAsync;
        return Task.CompletedTask;
    }

    private async Task _onUserJoinedAsync(SocketGuildUser user)
    {
        // If this is not the configured server
        if (user.Guild.Id != _serverId)
        {
            return;
        }
        
        // Get the guild
        var guild = client.GetGuild(_serverId);
        
        // Get the text channel
        var welcomeChannel = guild.GetTextChannel(_welcomeTextChannelId);
        
        // Get the message content
        var messageContent = _buildMessage(user, guild);

        await welcomeChannel.SendMessageAsync(messageContent).ConfigureAwait(false);
    }

    private string _buildMessage(SocketGuildUser user, SocketGuild guild)
    {
        // Replace the user placeholder with a mention to the user
        var messageText = _welcomeMessageTemplate.Replace("{{User}}", user.Mention);
        
        // Replace the guild name placeholder with the guild name
        messageText = messageText.Replace("{{Guild}}", guild.Name);
        
        // Replace the number of members placeholder with the number of members
        messageText = messageText.Replace("{{NumMember}}", guild.MemberCount.ToString());
        
        return messageText;
    }
    
    private readonly ulong _serverId = config.GetValue<ulong>(ConfigKeys.DiscordServerIdConfigurationKey);
    private readonly ulong _welcomeTextChannelId = config.GetValue<ulong>(ConfigKeys.DiscordWelcomeTextChannelIdConfigurationKey);
    private readonly string _welcomeMessageTemplate =
        config.GetValue<string>(ConfigKeys.DiscordWelcomeMessageConfigurationKey)!;
}