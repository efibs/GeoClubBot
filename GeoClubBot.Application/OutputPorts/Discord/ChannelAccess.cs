namespace UseCases.OutputPorts.Discord;

/// <summary>The rights a permission overwrite grants or denies, limited to the ones private channels use.</summary>
[Flags]
public enum ChannelRights
{
    None = 0,
    View = 1 << 0,
    SendMessages = 1 << 1,
    ReadMessageHistory = 1 << 2
}

public enum ChannelAccessTarget
{
    Role,
    User
}

/// <summary>One permission overwrite of a channel or category, in Discord-neutral terms.</summary>
public sealed record ChannelAccess(ulong TargetId, ChannelAccessTarget Target, ChannelRights Allow, ChannelRights Deny);

/// <summary>A channel of the server as the REST API reports it right now.</summary>
/// <param name="ParentCategoryId">The category the channel sits in, if any.</param>
public sealed record GuildChannelInfo(
    ulong Id,
    ulong? ParentCategoryId,
    bool IsCategory,
    IReadOnlyList<ChannelAccess> Access);

/// <summary>
/// Every channel of the server, read in one request, plus the two ids that private-channel overwrites
/// always name: <c>@everyone</c> (whose role id is the server id) and the bot itself.
/// </summary>
public sealed record GuildChannelLayout(ulong EveryoneRoleId, ulong BotUserId, IReadOnlyList<GuildChannelInfo> Channels);
