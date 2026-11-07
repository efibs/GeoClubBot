namespace UseCases.UseCases.ClubEvents;

/// <summary>
/// Where an event stands among the others: GeoGuessr's own timestamp when the frame had one, and the
/// order the bot received it in. A switch arrives as a leave on one club's socket and a join on the
/// other's, so arrival order alone can put them the wrong way round.
/// </summary>
public readonly record struct PushOrder(DateTimeOffset? ServerTime, long Sequence);

/// <summary>
/// Something GeoGuessr's socket reported about a club, or a refresh the socket service asks for. The
/// events of a batch are turned into work by <see cref="ClubEventPlanner"/>.
/// </summary>
public abstract record ClubPushEvent(Guid ClubId, PushOrder Order);

/// <summary><c>ClubMemberJoined</c> carrying every field the bot stores about a member.</summary>
public sealed record ClubMemberJoinedPush(
    Guid ClubId,
    PushOrder Order,
    string UserId,
    string Nickname,
    int Xp,
    DateTimeOffset JoinedAt) : ClubPushEvent(ClubId, Order);

/// <summary><c>ClubMemberLeft</c>. The member's other details come from the database.</summary>
public sealed record ClubMemberLeftPush(Guid ClubId, PushOrder Order, string UserId) : ClubPushEvent(ClubId, Order);

/// <summary>
/// A member event whose payload lacked what the bot needs (or payloads are switched off): the club's
/// roster changed, but only a re-read can tell how.
/// </summary>
public sealed record ClubRosterChangedPush(Guid ClubId, PushOrder Order) : ClubPushEvent(ClubId, Order);

/// <summary><c>ClubUpdated</c>: a partial club. A field it doesn't carry is unchanged.</summary>
public sealed record ClubUpdatedPush(Guid ClubId, PushOrder Order, int? Level, string? Name) : ClubPushEvent(ClubId, Order);

/// <summary><c>ClubMissionBoardUpdated</c>. It carries nothing; the board has to be read again.</summary>
public sealed record ClubMissionBoardUpdatedPush(Guid ClubId, PushOrder Order) : ClubPushEvent(ClubId, Order);

/// <summary>
/// The whole club should be read again — roster, mission board and level — because events may have
/// been missed (<see cref="ClubRefreshReason"/>).
/// </summary>
public sealed record ClubRefreshRequested(Guid ClubId, PushOrder Order, ClubRefreshReason Reason) : ClubPushEvent(ClubId, Order);

public enum ClubRefreshReason
{
    /// <summary>The socket (re)connected; whatever happened while it was down is unknown.</summary>
    Connected,

    /// <summary>The socket has been down for a polling interval.</summary>
    Disconnected,

    /// <summary>The socket is switched off for this club; this is its polling.</summary>
    SocketDisabled,

    /// <summary>Applying the previous batch failed.</summary>
    BatchRetry,
}
