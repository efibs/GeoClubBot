namespace UseCases.OutputPorts.GeoGuessr;

/// <summary>Why a delivery to a club's chat stopped, or that it went through.</summary>
public enum ClubChatDeliveryStatus
{
    /// <summary>Every part was sent and echoed back by GeoGuessr.</summary>
    Confirmed,

    /// <summary>The last part sent was not echoed back in time. It may still have arrived, so it is never resent.</summary>
    Unconfirmed,

    /// <summary>The bot could not reach GeoGuessr as the club's account, or the connection broke.</summary>
    NotConnected,

    /// <summary>GeoGuessr refused the club chat's topic to the club's account. Nothing was sent.</summary>
    Denied,
}

/// <param name="PartsSent">Parts sent, the one that was not confirmed included.</param>
/// <param name="PartsConfirmed">Parts GeoGuessr echoed back.</param>
/// <param name="Detail">What went wrong, for the admin and the log; null on success.</param>
public sealed record ClubChatDelivery(ClubChatDeliveryStatus Status, int PartsSent, int PartsConfirmed, string? Detail = null);

/// <summary>Writes into a club's in-game chat as the account of that club's token.</summary>
public interface IGeoGuessrClubChatSender
{
    /// <summary>
    /// Sends <paramref name="parts"/> in order, each only after GeoGuessr echoed the one before, and
    /// stops at the first that isn't. Never retries: a send that failed may have arrived anyway.
    /// </summary>
    Task<ClubChatDelivery> SendAsync(Guid clubId, IReadOnlyList<string> parts, TimeSpan echoTimeout, CancellationToken cancellationToken = default);
}
