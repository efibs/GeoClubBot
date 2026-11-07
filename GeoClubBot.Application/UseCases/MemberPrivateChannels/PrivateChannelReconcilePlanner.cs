using Entities;

namespace UseCases.UseCases.MemberPrivateChannels;

/// <summary>What a reconcile does to one member's private channel.</summary>
public enum PrivateChannelRepair
{
    /// <summary>In a club, linked, but no channel (never created, or deleted on Discord): create one.</summary>
    Create,

    /// <summary>In a club, but the channel is still archived: restore it into the club's category.</summary>
    Restore,

    /// <summary>In a club, live channel outside that club's categories (old shared category, other club): move it.</summary>
    MoveToClub,

    /// <summary>In no club, but the channel is still live: archive it.</summary>
    Archive,

    /// <summary>In no club, archived, but the channel sits outside the archive category: move it back.</summary>
    MoveToArchive,

    /// <summary>In no club and the channel no longer exists on Discord: forget its id.</summary>
    Forget
}

/// <param name="Repair">What to do with the channel's place, or null when it is where it belongs.</param>
/// <param name="FixAccess">Whether the channel's permission overwrites differ from the policy.</param>
public sealed record PlannedPrivateChannelRepair(ClubMember Member, PrivateChannelRepair? Repair, bool FixAccess);

/// <summary>
/// Decides, without touching Discord, what a reconcile must do to every member's private channel.
/// Pure, so the dry run reports exactly what the real run will attempt.
/// </summary>
public static class PrivateChannelReconcilePlanner
{
    public static IReadOnlyList<PlannedPrivateChannelRepair> Plan(
        IEnumerable<ClubMember> members,
        PrivateChannelPlacement placement,
        ulong archiveCategoryId,
        IReadOnlyCollection<ulong> adminRoleIds)
    {
        var plan = new List<PlannedPrivateChannelRepair>();

        foreach (var member in members.OrderBy(m => m.User.Nickname, StringComparer.OrdinalIgnoreCase))
        {
            // Without a linked Discord account nobody could see the channel; unlinking deletes it.
            if (member.User.DiscordUserId is not { } discordUserId)
            {
                continue;
            }

            var channelId = member.PrivateTextChannelId;
            var exists = placement.ChannelExists(channelId);
            PrivateChannelRepair? repair;

            if (member.ClubId is { } clubId)
            {
                repair = !exists ? PrivateChannelRepair.Create
                    : member.PrivateTextChannelArchivedAt is not null ? PrivateChannelRepair.Restore
                    : !placement.IsCategoryOf(clubId, placement.ParentOf(channelId!.Value)) ? PrivateChannelRepair.MoveToClub
                    : null;
            }
            else
            {
                if (channelId is null)
                {
                    continue;
                }

                repair = !exists ? PrivateChannelRepair.Forget
                    : member.PrivateTextChannelArchivedAt is null ? PrivateChannelRepair.Archive
                    : placement.ParentOf(channelId.Value) != archiveCategoryId ? PrivateChannelRepair.MoveToArchive
                    : null;
            }

            // A created channel gets the right overwrites from the start, and a restore re-applies them.
            var fixAccess = exists
                            && repair is not (PrivateChannelRepair.Create or PrivateChannelRepair.Restore)
                            && !PrivateChannelAccessPolicy.Matches(
                                placement.AccessOf(channelId!.Value),
                                PrivateChannelAccessPolicy.ForChannel(
                                    placement.EveryoneRoleId, placement.BotUserId, discordUserId, adminRoleIds));

            if (repair is not null || fixAccess)
            {
                plan.Add(new PlannedPrivateChannelRepair(member, repair, fixAccess));
            }
        }

        return plan;
    }

    /// <summary>
    /// How many new categories each club needs for the channels the plan puts into it, given the free
    /// slots its existing categories have. An estimate for the dry run: slots that moves out of a
    /// category would free are not counted.
    /// </summary>
    public static IReadOnlyDictionary<Guid, int> CategoriesToCreate(
        IEnumerable<PlannedPrivateChannelRepair> plan,
        PrivateChannelPlacement placement,
        int maxChannelsPerCategory)
    {
        return plan
            .Where(p => p.Repair is PrivateChannelRepair.Create or PrivateChannelRepair.Restore or PrivateChannelRepair.MoveToClub)
            .GroupBy(p => p.Member.ClubId!.Value)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var free = placement.CategoriesOf(g.Key)
                        .Sum(c => Math.Max(0, maxChannelsPerCategory - placement.CountIn(c.CategoryId)));
                    var overflow = Math.Max(0, g.Count() - free);
                    return (overflow + maxChannelsPerCategory - 1) / maxChannelsPerCategory;
                });
    }
}
