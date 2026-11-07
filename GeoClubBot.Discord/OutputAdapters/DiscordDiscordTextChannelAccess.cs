using Configuration;
using Discord;
using Discord.WebSocket;
using Entities;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.Discord;

namespace GeoClubBot.Discord.OutputAdapters;

public class DiscordDiscordTextChannelAccess(DiscordSocketClient client, IOptions<DiscordConfiguration> config)
    : IDiscordTextChannelAccess
{
    public async Task<ulong?> CreatePrivateTextChannelAsync(ulong categoryId, string name, string description,
        IReadOnlyCollection<ChannelAccess> access, CancellationToken cancellationToken = default)
    {
        var guild = client.GetGuild(config.Value.ServerId);

        var createdTextChannel = await guild.CreateTextChannelAsync(name, options =>
        {
            options.CategoryId = categoryId;
            options.Topic = description;

            // Set explicitly rather than synced from the category, so the channel stays private to
            // exactly these people wherever it is moved later.
            options.PermissionOverwrites = ToOverwrites(access);
        }, Request(cancellationToken)).ConfigureAwait(false);

        return createdTextChannel?.Id;
    }

    public async Task<ulong?> CreateCategoryAsync(string name, IReadOnlyCollection<ChannelAccess> access,
        CancellationToken cancellationToken = default)
    {
        var guild = client.GetGuild(config.Value.ServerId);

        var category = await guild.CreateCategoryChannelAsync(name,
                options => options.PermissionOverwrites = ToOverwrites(access),
                Request(cancellationToken))
            .ConfigureAwait(false);

        return category?.Id;
    }

    public async Task<GuildChannelLayout> ReadChannelLayoutAsync(CancellationToken cancellationToken = default)
    {
        var serverId = config.Value.ServerId;

        // REST rather than the gateway cache: a channel created or moved a moment ago must already be
        // counted in its new category, or two quick creates could both pick a category with one free slot.
        var guild = await client.Rest
                        .GetGuildAsync(serverId, Request(cancellationToken))
                        .ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Discord server {serverId} was not found.");

        var channels = await guild
            .GetChannelsAsync(Request(cancellationToken))
            .ConfigureAwait(false);

        var infos = channels
            .Select(c => new GuildChannelInfo(
                c.Id,
                (c as INestedChannel)?.CategoryId,
                c is ICategoryChannel,
                c.PermissionOverwrites.Select(ToAccess).ToList()))
            .ToList();

        return new GuildChannelLayout(guild.EveryoneRole.Id, client.CurrentUser.Id, infos);
    }

    public async Task<bool> SetChannelAccessAsync(ulong channelId, IReadOnlyCollection<ChannelAccess> access,
        CancellationToken cancellationToken = default)
    {
        var guild = client.GetGuild(config.Value.ServerId);

        // Text channels and categories alike.
        var channel = guild.GetChannel(channelId);

        if (channel == null)
        {
            return false;
        }

        await channel
            .ModifyAsync(options => options.PermissionOverwrites = ToOverwrites(access), Request(cancellationToken))
            .ConfigureAwait(false);

        return true;
    }

    public async Task<bool> UpdateTextChannelAsync(TextChannel newTextChannel, CancellationToken cancellationToken = default)
    {
        var guild = client.GetGuild(config.Value.ServerId);
        var textChannel = guild.GetTextChannel(newTextChannel.Id);

        if (textChannel == null)
        {
            return false;
        }

        // Discord allows only two renames of a channel per ten minutes and makes the third wait, so a
        // name that is already right is not sent again.
        var rename = string.IsNullOrWhiteSpace(newTextChannel.Name) == false
                     && string.Equals(textChannel.Name, newTextChannel.Name, StringComparison.Ordinal) == false;
        var retopic = string.IsNullOrWhiteSpace(newTextChannel.Description) == false;
        var move = newTextChannel.CategoryId is not null && textChannel.CategoryId != newTextChannel.CategoryId;

        if (!rename && !retopic && !move)
        {
            return true;
        }

        await textChannel.ModifyAsync(options =>
        {
            if (rename)
            {
                options.Name = newTextChannel.Name;
            }

            if (retopic)
            {
                options.Topic = newTextChannel.Description;
            }

            if (move)
            {
                // Explicit permission overwrites survive the move (the channel isn't synced to the
                // category), so a private channel stays private to the same people.
                options.CategoryId = newTextChannel.CategoryId!.Value;
            }
        }, Request(cancellationToken)).ConfigureAwait(false);

        return true;
    }

    public async Task<bool> DeleteTextChannelAsync(ulong textChannelId, CancellationToken cancellationToken = default)
    {
        var guild = client.GetGuild(config.Value.ServerId);
        var textChannel = guild.GetTextChannel(textChannelId);

        if (textChannel == null)
        {
            return false;
        }

        await textChannel.DeleteAsync(Request(cancellationToken)).ConfigureAwait(false);

        return true;
    }

    public async Task<bool> DeleteCategoryAsync(ulong categoryId, CancellationToken cancellationToken = default)
    {
        var guild = client.GetGuild(config.Value.ServerId);
        var category = guild.GetCategoryChannel(categoryId);

        if (category == null)
        {
            return false;
        }

        await category.DeleteAsync(Request(cancellationToken)).ConfigureAwait(false);

        return true;
    }

    public async Task<ulong?> ReadLastMessageOfUserAsync(ulong userId, ulong channelId, int numMessageSearchlimit,
        CancellationToken cancellationToken = default)
    {
        var guild = client.GetGuild(config.Value.ServerId);
        var textChannel = guild.GetTextChannel(channelId);

        if (textChannel == null)
        {
            return null;
        }

        var messages = await textChannel
            .GetMessagesAsync(numMessageSearchlimit)
            .FlattenAsync()
            .ConfigureAwait(false);

        var latestMessage = messages
            .Where(m => m.Author.Id == userId)
            .OrderByDescending(m => m.Timestamp)
            .FirstOrDefault();

        return latestMessage?.Id;
    }

    private static RequestOptions Request(CancellationToken cancellationToken) =>
        new() { CancelToken = cancellationToken };

    private static List<Overwrite> ToOverwrites(IEnumerable<ChannelAccess> access) =>
        access
            .Select(a => new Overwrite(
                a.TargetId,
                a.Target == ChannelAccessTarget.Role ? PermissionTarget.Role : PermissionTarget.User,
                new OverwritePermissions(ToBits(a.Allow), ToBits(a.Deny))))
            .ToList();

    private static ChannelAccess ToAccess(Overwrite overwrite) =>
        new(overwrite.TargetId,
            overwrite.TargetType == PermissionTarget.Role ? ChannelAccessTarget.Role : ChannelAccessTarget.User,
            FromBits(overwrite.Permissions.AllowValue),
            FromBits(overwrite.Permissions.DenyValue));

    private static readonly (ChannelRights Right, ChannelPermission Permission)[] RightMap =
    [
        (ChannelRights.View, ChannelPermission.ViewChannel),
        (ChannelRights.SendMessages, ChannelPermission.SendMessages),
        (ChannelRights.ReadMessageHistory, ChannelPermission.ReadMessageHistory)
    ];

    private static ulong ToBits(ChannelRights rights) =>
        RightMap
            .Where(m => rights.HasFlag(m.Right))
            .Aggregate(0UL, (bits, m) => bits | (ulong)m.Permission);

    // Bits outside the map are dropped: the bot only manages the rights it names, so an extra right an
    // admin granted by hand is neither reported as a difference nor kept when the bot rewrites the overwrites.
    private static ChannelRights FromBits(ulong bits) =>
        RightMap
            .Where(m => (bits & (ulong)m.Permission) != 0)
            .Aggregate(ChannelRights.None, (rights, m) => rights | m.Right);
}
