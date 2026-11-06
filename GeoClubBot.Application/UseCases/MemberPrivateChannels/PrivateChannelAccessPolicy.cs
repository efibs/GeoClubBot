using UseCases.OutputPorts.Discord;

namespace UseCases.UseCases.MemberPrivateChannels;

/// <summary>
/// Who may see a member's private channel and a club's category: the single place that decides, so
/// creating, restoring and reconciling all write and compare the same overwrites.
/// </summary>
public static class PrivateChannelAccessPolicy
{
    private const ChannelRights BotRights =
        ChannelRights.View | ChannelRights.SendMessages | ChannelRights.ReadMessageHistory;

    /// <summary>
    /// A member's channel: hidden from <c>@everyone</c>, visible to the bot, the member and the admin roles.
    /// Anyone with the Administrator permission sees it regardless.
    /// </summary>
    public static IReadOnlyList<ChannelAccess> ForChannel(ulong everyoneRoleId, ulong botUserId,
        ulong memberDiscordUserId, IEnumerable<ulong> adminRoleIds) =>
    [
        Hidden(everyoneRoleId),
        new(botUserId, ChannelAccessTarget.User, BotRights, ChannelRights.None),
        new(memberDiscordUserId, ChannelAccessTarget.User, ChannelRights.View, ChannelRights.None),
        .. Roles(everyoneRoleId, adminRoleIds)
    ];

    /// <summary>
    /// A club's category: hidden from <c>@everyone</c>, visible to the bot and the given roles (the club's
    /// role and the admin roles). Only the header depends on it — the channels inside carry their own
    /// overwrites, so a member sees their channel even while their club role is still missing.
    /// </summary>
    public static IReadOnlyList<ChannelAccess> ForCategory(ulong everyoneRoleId, ulong botUserId,
        IEnumerable<ulong> roleIds) =>
    [
        Hidden(everyoneRoleId),
        new(botUserId, ChannelAccessTarget.User, BotRights, ChannelRights.None),
        .. Roles(everyoneRoleId, roleIds)
    ];

    /// <summary>
    /// Whether <paramref name="actual"/> grants exactly what <paramref name="wanted"/> does. Order is
    /// irrelevant, and an overwrite that touches none of the rights the bot manages counts as absent.
    /// </summary>
    public static bool Matches(IEnumerable<ChannelAccess> actual, IEnumerable<ChannelAccess> wanted) =>
        Normalize(actual).SetEquals(Normalize(wanted));

    private static ChannelAccess Hidden(ulong everyoneRoleId) =>
        new(everyoneRoleId, ChannelAccessTarget.Role, ChannelRights.None, ChannelRights.View);

    private static IEnumerable<ChannelAccess> Roles(ulong everyoneRoleId, IEnumerable<ulong> roleIds) =>
        roleIds
            .Where(id => id != everyoneRoleId)
            .Distinct()
            .Select(id => new ChannelAccess(id, ChannelAccessTarget.Role, ChannelRights.View, ChannelRights.None));

    private static HashSet<ChannelAccess> Normalize(IEnumerable<ChannelAccess> access) =>
        access
            .Where(a => a.Allow != ChannelRights.None || a.Deny != ChannelRights.None)
            .ToHashSet();
}
