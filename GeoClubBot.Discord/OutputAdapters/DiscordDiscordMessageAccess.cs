using System.Text;
using Configuration;
using Constants;
using Discord;
using Discord.WebSocket;
using Entities;
using Extensions;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.Discord;

namespace GeoClubBot.Discord.OutputAdapters;

public class DiscordDiscordMessageAccess(DiscordSocketClient client, IOptions<DiscordConfiguration> config)
    : IDiscordMessageAccess
{
    private const int DiscordMessageLimit = 2000;

    public async Task SendMessageAsync(string message, ulong channelId, CancellationToken cancellationToken = default)
    {
        var (_, channel) = GetTextChannel(channelId);

        // Send the message
        await channel.SendMessageAsync(message).ConfigureAwait(false);
    }

    public async Task<ulong> SendMessageAsync(
        string message,
        ulong channelId,
        MessageMentions allowedMentions,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Discord does not accept an empty message.", nameof(message));
        }

        var (_, channel) = GetTextChannel(channelId);
        var mentions = ToAllowedMentions(allowedMentions);

        ulong lastMessageId = 0;
        foreach (var chunk in message.SplitAtCharWithLimit("\n", DiscordMessageLimit))
        {
            var sent = await channel.SendMessageAsync(chunk, allowedMentions: mentions).ConfigureAwait(false);
            lastMessageId = sent.Id;
        }

        return lastMessageId;
    }

    public async Task CreateThreadAsync(
        ulong channelId,
        ulong messageId,
        string name,
        ThreadAutoArchive autoArchive,
        CancellationToken cancellationToken = default)
    {
        var (_, channel) = GetTextChannel(channelId);

        var message = await channel.GetMessageAsync(messageId).ConfigureAwait(false)
                      ?? throw new InvalidOperationException($"No message found for id {messageId} in channel {channelId}");

        await channel
            .CreateThreadAsync(name, ThreadType.PublicThread, ToArchiveDuration(autoArchive), message)
            .ConfigureAwait(false);
    }

    public async Task SendSelfRolesMessageAsync(ulong channelId, IEnumerable<SelfRoleSetting> selfRoleSettings,
        CancellationToken cancellationToken = default)
    {
        var (server, channel) = GetTextChannel(channelId);

        // Build the message content
        var msg = await BuildSelfRoleMessageContent(selfRoleSettings, server).ConfigureAwait(false);

        // Build the button component
        var button = new ComponentBuilder()
            .WithButton("Select roles", customId: ComponentIds.SelfRolesSelectButtonId)
            .Build();

        // Send the message
        await channel
            .SendMessageAsync(msg, components: button)
            .ConfigureAwait(false);
    }

    public async Task UpdateSelfRolesMessageAsync(ulong channelId, ulong messageId,
        IEnumerable<SelfRoleSetting> selfRoleSettings, CancellationToken cancellationToken = default)
    {
        var (server, channel) = GetTextChannel(channelId);

        // Build the message content
        var msg = await BuildSelfRoleMessageContent(selfRoleSettings, server).ConfigureAwait(false);

        // Get the message
        var message = await channel.GetMessageAsync(messageId).ConfigureAwait(false);

        // If the message does not exist
        if (message == null)
        {
            throw new InvalidOperationException($"No message found for id {messageId}");
        }

        // If the message content is already up to date
        if (message.Content == msg)
        {
            // Nothing to do
            return;
        }

        // Update the message
        await channel
            .ModifyMessageAsync(messageId, m => m.Content = msg)
            .ConfigureAwait(false);
    }

    public async Task DeleteMessageAsync(ulong messageId, ulong channelId,
        CancellationToken cancellationToken = default)
    {
        var (_, channel) = GetTextChannel(channelId);

        // Get the message
        var message = await channel.GetMessageAsync(messageId).ConfigureAwait(false);

        // Sanity check
        if (message == null)
        {
            throw new InvalidOperationException($"No message found for id {messageId} in channel {channelId}");
        }

        // Delete the message
        await message.DeleteAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Pings only the listed roles and users, and @everyone/@here only when asked. Leaving allowed
    /// mentions unset would let Discord ping every mention it finds in the text.
    /// </summary>
    public static AllowedMentions ToAllowedMentions(MessageMentions mentions)
    {
        return new AllowedMentions(mentions.Everyone ? AllowedMentionTypes.Everyone : AllowedMentionTypes.None)
        {
            RoleIds = [.. mentions.RoleIds],
            UserIds = [.. mentions.UserIds]
        };
    }

    public static ThreadArchiveDuration ToArchiveDuration(ThreadAutoArchive autoArchive) => autoArchive switch
    {
        ThreadAutoArchive.OneHour => ThreadArchiveDuration.OneHour,
        ThreadAutoArchive.ThreeDays => ThreadArchiveDuration.ThreeDays,
        ThreadAutoArchive.OneWeek => ThreadArchiveDuration.OneWeek,
        _ => ThreadArchiveDuration.OneDay
    };

    private (SocketGuild Server, SocketTextChannel Channel) GetTextChannel(ulong channelId)
    {
        // Get the server
        var server = client.GetGuild(config.Value.ServerId)
                     ?? throw new InvalidOperationException($"No server found for id {config.Value.ServerId}");

        // Get the channel
        var channel = server.GetTextChannel(channelId)
                      ?? throw new InvalidOperationException($"No channel found for id {channelId}");

        return (server, channel);
    }

    private static async Task<string> BuildSelfRoleMessageContent(IEnumerable<SelfRoleSetting> selfRoleSettings,
        SocketGuild server)
    {
        // Build the message
        var msgBuilder = new StringBuilder("# Select the roles you would like to have\nThe available roles are:\n");

        // For every setting
        foreach (var roleSetting in selfRoleSettings)
        {
            // Get the role name
            var role = await server.GetRoleAsync(roleSetting.RoleId).ConfigureAwait(false);

            // If the role has an icon set
            if (string.IsNullOrWhiteSpace(roleSetting.RoleEmoji) == false)
            {
                msgBuilder.Append(roleSetting.RoleEmoji);
            }
            else
            {
                msgBuilder.Append("\t  ");
            }

            msgBuilder.Append(' ');
            msgBuilder.Append(role.Name);

            // If the role has a description set
            if (string.IsNullOrWhiteSpace(roleSetting.RoleDescription) == false)
            {
                msgBuilder.Append(": ");
                msgBuilder.Append(roleSetting.RoleDescription);
            }

            msgBuilder.AppendLine();
        }

        // Build the message
        var msg = msgBuilder.ToString().Trim();

        return msg;
    }
}
