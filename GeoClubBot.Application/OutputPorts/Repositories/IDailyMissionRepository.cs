using Entities;

namespace UseCases.OutputPorts.Repositories;

public interface IDailyMissionRepository
{
    void AddRange(IEnumerable<DailyMission> missions);

    Task<IReadOnlyList<DailyMission>> ReadLatestFetchedMissionsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<DailyMission>> ReadMissionsFetchedBetweenAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DailyMissionKind>> ReadDistinctMissionKindsAsync(CancellationToken cancellationToken);
}

public sealed record DailyMissionKind(string Type, string GameMode);
