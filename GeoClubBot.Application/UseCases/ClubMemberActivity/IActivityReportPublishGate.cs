namespace UseCases.UseCases.ClubMemberActivity;

/// <summary>
/// Serializes the message-publishing tail of <see cref="CheckGeoGuessrPlayerActivityHandler"/>.
/// Clubs are checked in parallel (each in its own DI scope), but every club posts its activity
/// report messages to the same shared channel. A club emits several messages in sequence (status
/// header, player chunks, individual targets, average XP); without serialization those sequences
/// interleave between clubs.
/// </summary>
/// <remarks>
/// Lives as a singleton so the lock is shared across the per-club scopes. A handler holds the
/// gate across its whole publish region, guaranteeing that all messages for one club are sent
/// before any other club's messages begin.
/// </remarks>
public interface IActivityReportPublishGate
{
    /// <summary>
    /// Waits for exclusive access to the report channel. Dispose the returned handle to release it.
    /// </summary>
    Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken = default);
}
