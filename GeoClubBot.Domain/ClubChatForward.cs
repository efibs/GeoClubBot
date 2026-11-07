namespace Entities;

/// <summary>
/// One forward of a Discord message into a club's in-game chat. Kept so that forwarding the same
/// message again warns who already sent it, and when.
/// </summary>
public class ClubChatForward : BaseEntity
{
    public int Id { get; private set; }

    public ulong DiscordMessageId { get; private set; }

    public Guid ClubId { get; private set; }

    public ulong ForwardedByDiscordUserId { get; private set; }

    public DateTimeOffset ForwardedAt { get; private set; }

    /// <summary>How many parts the text was split into.</summary>
    public int PartCount { get; private set; }

    /// <summary>Parts sent, the first one that failed included: it may have arrived anyway.</summary>
    public int PartsSent { get; private set; }

    /// <summary>Parts GeoGuessr echoed back.</summary>
    public int PartsConfirmed { get; private set; }

    public static ClubChatForward Create(
        ulong discordMessageId,
        Guid clubId,
        ulong forwardedByDiscordUserId,
        DateTimeOffset forwardedAt,
        int partCount,
        int partsSent,
        int partsConfirmed) =>
        new()
        {
            DiscordMessageId = discordMessageId,
            ClubId = clubId,
            ForwardedByDiscordUserId = forwardedByDiscordUserId,
            ForwardedAt = forwardedAt,
            PartCount = partCount,
            PartsSent = partsSent,
            PartsConfirmed = partsConfirmed,
        };

    private ClubChatForward()
    {
    }
}
