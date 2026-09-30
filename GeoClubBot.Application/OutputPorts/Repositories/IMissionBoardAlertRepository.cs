using Entities;

namespace UseCases.OutputPorts.Repositories;

public interface IMissionBoardAlertRepository
{
    /// <summary>The (mission, kind) pairs of <paramref name="clubId"/> already alerted about.</summary>
    Task<HashSet<(Guid MissionId, MissionBoardAlertKind Kind)>> ReadSentAlertsAsync(
        Guid clubId,
        IReadOnlyCollection<Guid> missionIds,
        CancellationToken cancellationToken = default);

    void Add(MissionBoardAlert alert);

    /// <summary>Deletes alerts sent before <paramref name="threshold"/>; their missions are long gone.</summary>
    Task<int> DeleteSentBeforeAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default);
}
