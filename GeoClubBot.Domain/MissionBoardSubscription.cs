namespace Entities;

/// <summary>How long a <see cref="MissionBoardSubscription"/> lasts.</summary>
public enum MissionBoardSubscriptionMode
{
    /// <summary>Every new board and every new week, until the member turns it off.</summary>
    Always = 0,

    /// <summary>Only the next new board (or new week); the subscription ends with that DM.</summary>
    NextChangeOnly = 1
}

/// <summary>
/// A member who wants a DM when their club's mission board moves on: the next board is unlocked or a
/// new week starts (#504). Keyed by Discord user: the club is whichever one their linked account is
/// in when the board changes, so a club switch needs nothing.
/// </summary>
public class MissionBoardSubscription : BaseEntity
{
    public ulong DiscordUserId { get; private set; }

    public MissionBoardSubscriptionMode Mode { get; private set; }

    public DateTimeOffset SubscribedAt { get; private set; }

    public static MissionBoardSubscription Create(ulong discordUserId, MissionBoardSubscriptionMode mode, DateTimeOffset now) =>
        new()
        {
            DiscordUserId = discordUserId,
            Mode = mode,
            SubscribedAt = now
        };

    public void ChangeMode(MissionBoardSubscriptionMode mode, DateTimeOffset now)
    {
        Mode = mode;
        SubscribedAt = now;
    }

    private MissionBoardSubscription()
    {
    }
}
