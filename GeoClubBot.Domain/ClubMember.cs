using Entities.Events;

namespace Entities;

public class ClubMember : BaseEntity
{
    public string UserId { get; private set; } = string.Empty;

    public Guid? ClubId { get; private set; }

    public GeoGuessrUser User { get; private set; } = null!;

    public int Xp { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    public ulong? PrivateTextChannelId { get; private set; }

    /// <summary>
    /// When the private text channel was moved to the archive category because the member left
    /// every club. Null while the channel is live, or while there is no channel at all.
    /// </summary>
    public DateTimeOffset? PrivateTextChannelArchivedAt { get; private set; }

    public List<ClubMemberHistoryEntry> History { get; private set; } = [];

    public List<ClubMemberStrike> Strikes { get; private set; } = [];

    public List<ClubMemberExcuse> Excuses { get; private set; } = [];

    public static ClubMember Create(GeoGuessrUser user, Guid? clubId, int xp, DateTimeOffset joinedAt)
    {
        var member = new ClubMember
        {
            UserId = user.UserId,
            User = user,
            ClubId = clubId,
            Xp = xp,
            JoinedAt = joinedAt
        };

        if (clubId is not null)
        {
            member.AddDomainEvent(new PlayerJoinedClubEvent(
                user.UserId, user.Nickname, user.DiscordUserId, clubId.Value, null));
        }

        return member;
    }

    public void SyncFromApi(Guid? newClubId, int newXp, DateTimeOffset newJoinedAt)
    {
        var oldClubId = ClubId;

        Xp = newXp;
        JoinedAt = newJoinedAt;
        ClubId = newClubId;

        if (oldClubId is not null && newClubId is null)
        {
            AddDomainEvent(new PlayerLeftClubEvent(
                UserId, User.Nickname, User.DiscordUserId, oldClubId.Value, PrivateTextChannelId));
        }
        else if (oldClubId is null && newClubId is not null)
        {
            AddDomainEvent(new PlayerJoinedClubEvent(
                UserId, User.Nickname, User.DiscordUserId, newClubId.Value, PrivateTextChannelId));
        }
        else if (oldClubId is not null && newClubId is not null && oldClubId != newClubId)
        {
            AddDomainEvent(new PlayerSwitchedClubsEvent(
                UserId, User.Nickname, User.DiscordUserId, oldClubId.Value, newClubId.Value));
        }
    }

    public void SetPrivateTextChannelId(ulong? channelId)
    {
        PrivateTextChannelId = channelId;

        // A channel that was just created or just deleted is never still archived.
        PrivateTextChannelArchivedAt = null;
    }

    public void ArchivePrivateTextChannel(DateTimeOffset archivedAt) => PrivateTextChannelArchivedAt = archivedAt;

    public void RestorePrivateTextChannel() => PrivateTextChannelArchivedAt = null;

    private ClubMember()
    {
    }

    public override string ToString() => User.ToString();
}
