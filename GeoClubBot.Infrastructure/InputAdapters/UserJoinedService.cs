using Configuration;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Infrastructure.InputAdapters;

public class UserJoinedService(DiscordSocketClient client, IOptions<DiscordConfiguration> discordOptions) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        client.UserJoined += OnUserJoinedAsync;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        client.UserJoined -= OnUserJoinedAsync;
        return Task.CompletedTask;
    }

    private async Task OnUserJoinedAsync(SocketGuildUser user)
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
        var messageContent = BuildMessage(user, guild);

        await welcomeChannel.SendMessageAsync(messageContent).ConfigureAwait(false);
    }

    private string BuildMessage(SocketGuildUser user, SocketGuild guild)
    {
        // Replace the user placeholder with a mention to the user
        var messageText = _welcomeMessageTemplate.Replace("{{User}}", user.Mention);
        
        // Replace the guild name placeholder with the guild name
        messageText = messageText.Replace("{{Guild}}", guild.Name);
        
        // Replace the number of members placeholder with the number of members
        messageText = messageText.Replace("{{NumMember}}", guild.MemberCount.ToString());
        
        return messageText;
    }
    
    private readonly ulong _serverId = discordOptions.Value.ServerId;
    private readonly ulong _welcomeTextChannelId = discordOptions.Value.WelcomeTextChannelId;
    private readonly string _welcomeMessageTemplate = discordOptions.Value.WelcomeMessage;
}