using UseCases.OutputPorts;

namespace UseCases.UseCases.Club;

/// <summary>
/// In-memory cache of the last-observed level for each configured club. Lives as a
/// singleton so the change-detection in <see cref="CheckClubLevelCommand"/> survives across
/// scoped handler invocations.
/// </summary>
public interface IClubLevelTracker
{
    /// <summary>
    /// Seeds the tracker from the database on the first call. Subsequent calls are no-ops.
    /// </summary>
    Task EnsureInitializedAsync(IClubRepository clubs, IEnumerable<Guid> clubIds, CancellationToken cancellationToken = default);

    int? TryGet(Guid clubId);

    void Set(Guid clubId, int level);
}
