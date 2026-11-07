namespace UseCases.UseCases.ClubMembers;

/// <summary>
/// Serialises every path that writes club membership: the scheduled roster sync, the weekly activity
/// check's roster step, the on-demand member lookup and the socket's push events. Each of them reads
/// a roster (from GeoGuessr or from a push), compares it with the database and saves the difference;
/// two of them interleaving would insert a member twice or apply the older roster last.
/// </summary>
/// <remarks>
/// Hold the gate from before the roster is read until <c>Send(SaveClubMembersCommand)</c> has
/// returned: the save, and the domain events it raises, only happen once that command's pipeline has
/// finished, so a lock inside the handler would be released too early.
/// <para>
/// Not re-entrant. Acquiring it again from the flow that already holds it throws instead of
/// deadlocking, so a future handler that syncs members from inside a member-change event fails loudly.
/// </para>
/// </remarks>
public interface IClubMemberSyncGate
{
    /// <summary>Waits for exclusive access. Dispose the returned handle to release it.</summary>
    Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken = default);
}
