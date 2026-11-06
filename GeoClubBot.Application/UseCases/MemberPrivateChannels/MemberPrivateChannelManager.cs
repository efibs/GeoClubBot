using Configuration;
using Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.MemberPrivateChannels;

/// <summary>
/// The operations on a member's private channel that decide which category it lives in: create,
/// archive, restore, move to the member's club. Every club has its own categories, created on demand
/// and numbered when one is full; the archive makes room by deleting the channel archived the longest.
/// Callers hold <see cref="PrivateChannelPlacementLock"/> and pass one <see cref="PrivateChannelPlacement"/>
/// through all the operations of a run. Database changes are left to the caller's unit of work, except
/// a new category, which is saved at once so a later failure can't orphan it on Discord.
/// </summary>
public sealed partial class MemberPrivateChannelManager(
    IDiscordTextChannelAccess channels,
    IDiscordMessageAccess messages,
    IClubMemberRepository clubMembers,
    IClubRepository clubs,
    IMemberPrivateChannelCategoryRepository categories,
    IUnitOfWork unitOfWork,
    IOptions<MemberPrivateChannelsConfiguration> options,
    IOptions<GeoGuessrConfiguration> geoGuessrOptions,
    ILogger<MemberPrivateChannelManager> logger)
{
    private MemberPrivateChannelsConfiguration Config => options.Value;

    public async Task<PrivateChannelPlacement> LoadAsync(CancellationToken cancellationToken)
    {
        var layout = await channels.ReadChannelLayoutAsync(cancellationToken).ConfigureAwait(false);
        var rows = await categories.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        var placement = new PrivateChannelPlacement(layout, rows);

        // An admin deleted one of the bot's categories: forget it, so its number can be reused.
        foreach (var vanished in placement.Vanished)
        {
            LogCategoryVanished(logger, vanished.CategoryId, vanished.ClubId);
            await categories.DeleteAsync(vanished.CategoryId, cancellationToken).ConfigureAwait(false);
        }

        return placement;
    }

    public IReadOnlyList<ChannelAccess> AccessFor(PrivateChannelPlacement placement, ulong memberDiscordUserId) =>
        PrivateChannelAccessPolicy.ForChannel(
            placement.EveryoneRoleId, placement.BotUserId, memberDiscordUserId, Config.AdminRoleIds);

    public async Task<Result<ulong>> CreateAsync(PrivateChannelPlacement placement, ClubMember clubMember,
        CancellationToken cancellationToken)
    {
        if (clubMember.ClubId is null || clubMember.User.DiscordUserId is null)
        {
            return Error.Validation(
                "member_private_channel.not_eligible",
                "Only a member of a club with a linked Discord account gets a private channel.");
        }

        var category = await CategoryForClubAsync(placement, clubMember.ClubId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (category.IsFailure)
        {
            return category.Error;
        }

        var access = AccessFor(placement, clubMember.User.DiscordUserId.Value);
        var channelId = await channels.CreatePrivateTextChannelAsync(
                category.Value,
                MemberPrivateChannelName.For(clubMember.User.Nickname),
                Config.Description,
                access,
                cancellationToken)
            .ConfigureAwait(false);

        if (channelId is null)
        {
            return Error.Unexpected(
                "member_private_channel.create_failed",
                "Discord did not create the private channel.");
        }

        placement.Placed(channelId.Value, category.Value);
        placement.AccessSet(channelId.Value, access);

        var trackedMember = await clubMembers
            .ReadForUpdateByUserIdAsync(clubMember.UserId, cancellationToken)
            .ConfigureAwait(false);
        trackedMember?.SetPrivateTextChannelId(channelId.Value);

        var welcome = $"Welcome <@{clubMember.User.DiscordUserId.Value}>! This is your " +
                      "private space to talk to our admins. Only you and the admins can see the messages in this " +
                      "text channel. Use this channel for example to talk about when you need an excuse for the " +
                      "club XP rule or any other concerns you might have.";
        await messages.SendMessageAsync(welcome, channelId.Value, cancellationToken).ConfigureAwait(false);

        return channelId.Value;
    }

    /// <summary>
    /// Moves the channel into the archive and starts its retention clock. A channel that is already
    /// archived keeps its clock; it is only moved back into the archive if it sits elsewhere.
    /// </summary>
    public async Task<Result> ArchiveAsync(PrivateChannelPlacement placement, ClubMember clubMember,
        CancellationToken cancellationToken)
    {
        var channelId = clubMember.PrivateTextChannelId!.Value;
        var alreadyArchived = clubMember.PrivateTextChannelArchivedAt is not null;
        var archiveId = Config.ArchiveCategoryId;

        if (placement.ParentOf(channelId) != archiveId)
        {
            var room = await EnsureArchiveSlotAsync(placement, cancellationToken).ConfigureAwait(false);
            if (room.IsFailure)
            {
                return room;
            }

            var moved = await channels
                .UpdateTextChannelAsync(new TextChannel(channelId) { CategoryId = archiveId }, cancellationToken)
                .ConfigureAwait(false);

            if (!moved)
            {
                return Error.Unexpected(
                    "member_private_channel.archive_failed",
                    "The Discord channel could not be moved to the archive category.");
            }

            placement.Placed(channelId, archiveId);
        }

        if (alreadyArchived)
        {
            return Result.Success();
        }

        var archivedAt = DateTimeOffset.UtcNow;
        var trackedMember = await clubMembers
            .ReadForUpdateByUserIdAsync(clubMember.UserId, cancellationToken)
            .ConfigureAwait(false);
        trackedMember?.ArchivePrivateTextChannel(archivedAt);

        // Deliberately no mention: nobody wants a ping for having been kicked.
        var deletionDate = archivedAt.Add(Config.ArchiveKeepTimeSpan);
        var notice = $"{clubMember.User.Nickname} is no longer in one of our clubs, so this channel " +
                     "has been archived. It stays readable for both sides, so we can still talk here. " +
                     $"If nobody rejoins a club it will be deleted on {deletionDate:yyyy-MM-dd}.";
        await messages.SendMessageAsync(notice, channelId, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    /// <summary>
    /// Brings an archived channel back into the member's club category, renamed to the current
    /// nickname (nickname changes are skipped while archived) and with its permissions re-applied.
    /// </summary>
    public async Task<Result> RestoreAsync(PrivateChannelPlacement placement, ClubMember clubMember,
        CancellationToken cancellationToken)
    {
        if (clubMember.ClubId is null)
        {
            return Error.Validation(
                "member_private_channel.not_in_club",
                "Only the channel of a member who is in a club can be restored.");
        }

        var channelId = clubMember.PrivateTextChannelId!.Value;
        ulong? targetCategory = null;
        if (!placement.IsCategoryOf(clubMember.ClubId.Value, placement.ParentOf(channelId)))
        {
            var category = await CategoryForClubAsync(placement, clubMember.ClubId.Value, cancellationToken)
                .ConfigureAwait(false);
            if (category.IsFailure)
            {
                return category.Error;
            }

            targetCategory = category.Value;
        }

        var channel = new TextChannel(channelId)
        {
            Name = MemberPrivateChannelName.For(clubMember.User.Nickname),
            CategoryId = targetCategory
        };

        var moved = await channels.UpdateTextChannelAsync(channel, cancellationToken).ConfigureAwait(false);
        if (!moved)
        {
            return Error.Unexpected(
                "member_private_channel.restore_failed",
                "The Discord channel could not be moved out of the archive category.");
        }

        if (channel.CategoryId is not null)
        {
            placement.Placed(channelId, channel.CategoryId.Value);
        }

        await EnsureAccessAsync(placement, clubMember, cancellationToken).ConfigureAwait(false);

        var trackedMember = await clubMembers
            .ReadForUpdateByUserIdAsync(clubMember.UserId, cancellationToken)
            .ConfigureAwait(false);
        trackedMember?.RestorePrivateTextChannel();

        if (clubMember.User.DiscordUserId is not null)
        {
            await messages
                .SendMessageAsync(
                    $"Welcome back <@{clubMember.User.DiscordUserId.Value}>! Your private channel is active again.",
                    channelId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return Result.Success();
    }

    /// <summary>
    /// Moves a live channel into one of its member's club categories, e.g. after a club switch.
    /// Returns whether it had to move.
    /// </summary>
    public async Task<Result<bool>> MoveToClubAsync(PrivateChannelPlacement placement, ClubMember clubMember,
        CancellationToken cancellationToken)
    {
        if (clubMember.ClubId is null)
        {
            return Error.Validation(
                "member_private_channel.not_in_club",
                "Only the channel of a member who is in a club can move to a club category.");
        }

        var channelId = clubMember.PrivateTextChannelId!.Value;
        if (placement.IsCategoryOf(clubMember.ClubId.Value, placement.ParentOf(channelId)))
        {
            return false;
        }

        var category = await CategoryForClubAsync(placement, clubMember.ClubId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (category.IsFailure)
        {
            return category.Error;
        }

        var moved = await channels
            .UpdateTextChannelAsync(new TextChannel(channelId) { CategoryId = category.Value }, cancellationToken)
            .ConfigureAwait(false);
        if (!moved)
        {
            return Error.Unexpected(
                "member_private_channel.move_failed",
                "The Discord channel could not be moved to the club's category.");
        }

        placement.Placed(channelId, category.Value);
        return true;
    }

    /// <summary>Re-applies the channel's permissions if they differ from the policy. Returns whether they did.</summary>
    public async Task<bool> EnsureAccessAsync(PrivateChannelPlacement placement, ClubMember clubMember,
        CancellationToken cancellationToken)
    {
        if (clubMember.PrivateTextChannelId is not { } channelId || clubMember.User.DiscordUserId is not { } userId)
        {
            return false;
        }

        var wanted = AccessFor(placement, userId);
        if (PrivateChannelAccessPolicy.Matches(placement.AccessOf(channelId), wanted))
        {
            return false;
        }

        if (await channels.SetChannelAccessAsync(channelId, wanted, cancellationToken).ConfigureAwait(false))
        {
            placement.AccessSet(channelId, wanted);
            return true;
        }

        return false;
    }

    /// <summary>Re-applies the permissions of every club category that drifted (e.g. a new club role). Returns how many.</summary>
    public async Task<int> EnsureCategoryAccessAsync(PrivateChannelPlacement placement,
        CancellationToken cancellationToken)
    {
        var fixedCount = 0;
        foreach (var category in placement.Categories.ToList())
        {
            var wanted = CategoryAccess(placement, category.ClubId);
            if (PrivateChannelAccessPolicy.Matches(placement.AccessOf(category.CategoryId), wanted))
            {
                continue;
            }

            if (await channels.SetChannelAccessAsync(category.CategoryId, wanted, cancellationToken).ConfigureAwait(false))
            {
                placement.AccessSet(category.CategoryId, wanted);
                fixedCount++;
            }
        }

        return fixedCount;
    }

    /// <summary>
    /// Deletes the overflow categories (number 2 and up) that are empty now. A club's first category is
    /// always kept, so a club never flips between having one and not.
    /// </summary>
    public async Task<int> PruneEmptyCategoriesAsync(PrivateChannelPlacement placement,
        CancellationToken cancellationToken)
    {
        var pruned = 0;
        foreach (var category in placement.Categories.Where(c => c.Index > 1).ToList())
        {
            if (placement.CountIn(category.CategoryId) > 0)
            {
                continue;
            }

            LogPruningCategory(logger, category.CategoryId, category.ClubId, category.Index);
            await channels.DeleteCategoryAsync(category.CategoryId, cancellationToken).ConfigureAwait(false);
            await categories.DeleteAsync(category.CategoryId, cancellationToken).ConfigureAwait(false);
            placement.CategoryRemoved(category.CategoryId);
            pruned++;
        }

        return pruned;
    }

    private async Task<Result<ulong>> CategoryForClubAsync(PrivateChannelPlacement placement, Guid clubId,
        CancellationToken cancellationToken)
    {
        var withRoom = placement
            .CategoriesOf(clubId)
            .FirstOrDefault(c => placement.CountIn(c.CategoryId) < Config.MaxChannelsPerCategory);

        if (withRoom is not null)
        {
            return withRoom.CategoryId;
        }

        var index = placement.NextIndexOf(clubId);
        var club = await clubs.ReadClubByIdAsync(clubId, cancellationToken).ConfigureAwait(false);
        var name = Config.CategoryName(club?.Name ?? clubId.ToString(), index);
        var access = CategoryAccess(placement, clubId);

        LogCreatingCategory(logger, name, clubId);

        var categoryId = await channels.CreateCategoryAsync(name, access, cancellationToken).ConfigureAwait(false);
        if (categoryId is null)
        {
            return Error.Unexpected(
                "member_private_channel.category_create_failed",
                $"Discord did not create the category '{name}'.");
        }

        var row = MemberPrivateChannelCategory.Create(categoryId.Value, clubId, index);
        categories.Add(row);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        placement.CategoryAdded(row, access);

        return categoryId.Value;
    }

    private IReadOnlyList<ChannelAccess> CategoryAccess(PrivateChannelPlacement placement, Guid clubId)
    {
        var clubRoleId = geoGuessrOptions.Value.Clubs?.FirstOrDefault(c => c.ClubId == clubId)?.RoleId;
        IEnumerable<ulong> roles = clubRoleId is null ? Config.AdminRoleIds : [clubRoleId.Value, .. Config.AdminRoleIds];

        return PrivateChannelAccessPolicy.ForCategory(placement.EveryoneRoleId, placement.BotUserId, roles);
    }

    /// <summary>
    /// Deletes the longest-archived channels until the archive has a free slot. Only channels that
    /// really sit in the archive free one; if the archive is full of channels the bot doesn't own,
    /// there is nothing it may delete.
    /// </summary>
    private async Task<Result> EnsureArchiveSlotAsync(PrivateChannelPlacement placement,
        CancellationToken cancellationToken)
    {
        var archiveId = Config.ArchiveCategoryId;
        if (placement.CountIn(archiveId) < Config.MaxChannelsPerCategory)
        {
            return Result.Success();
        }

        var oldestFirst = await clubMembers
            .ReadMembersWithArchivedPrivateChannelsOldestFirstAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var archived in oldestFirst)
        {
            if (placement.CountIn(archiveId) < Config.MaxChannelsPerCategory)
            {
                break;
            }

            var channelId = archived.PrivateTextChannelId!.Value;
            if (placement.ParentOf(channelId) != archiveId)
            {
                continue;
            }

            LogEvictingArchivedChannel(logger, archived.User.Nickname, archived.PrivateTextChannelArchivedAt);

            await channels.DeleteTextChannelAsync(channelId, cancellationToken).ConfigureAwait(false);
            placement.Removed(channelId);

            var tracked = await clubMembers
                .ReadForUpdateByUserIdAsync(archived.UserId, cancellationToken)
                .ConfigureAwait(false);
            tracked?.SetPrivateTextChannelId(null);
        }

        if (placement.CountIn(archiveId) >= Config.MaxChannelsPerCategory)
        {
            return Error.Conflict(
                "member_private_channel.archive_full",
                "The archive category is full and holds no archived member channel that could make room.");
        }

        return Result.Success();
    }

    [LoggerMessage(LogLevel.Warning,
        "Private channel category {CategoryId} of club {ClubId} no longer exists on Discord; forgetting it.")]
    static partial void LogCategoryVanished(ILogger<MemberPrivateChannelManager> logger, ulong categoryId, Guid clubId);

    [LoggerMessage(LogLevel.Information, "Creating private channel category '{CategoryName}' for club {ClubId}...")]
    static partial void LogCreatingCategory(ILogger<MemberPrivateChannelManager> logger, string categoryName, Guid clubId);

    [LoggerMessage(LogLevel.Information, "Deleting empty private channel category {CategoryId} (club {ClubId}, number {Index}).")]
    static partial void LogPruningCategory(ILogger<MemberPrivateChannelManager> logger, ulong categoryId, Guid clubId, int index);

    [LoggerMessage(LogLevel.Warning,
        "The archive category is full. Deleting the private channel of '{clubMemberNickname}', archived since {ArchivedAt}, to make room.")]
    static partial void LogEvictingArchivedChannel(ILogger<MemberPrivateChannelManager> logger, string clubMemberNickname,
        DateTimeOffset? archivedAt);
}
