using Entities;

namespace UseCases.OutputPorts.Repositories;

public interface IDailyMissionCompletionRepository
{
    void AddRange(IEnumerable<DailyMissionMemberCompletion> completions);

    Task<bool> HasSnapshotForDayAsync(Guid clubId, DateOnly day, CancellationToken cancellationToken);

    /// <summary>
    /// Reads all per-member completion rows whose <see cref="DailyMissionMemberCompletion.Date"/>
    /// lies in [<paramref name="fromDay"/>, <paramref name="toDay"/>].
    /// A <c>null</c> <paramref name="clubId"/> reads across all clubs.
    /// </summary>
    Task<IReadOnlyList<DailyMissionMemberCompletion>> ReadCompletionsAsync(
        Guid? clubId,
        DateOnly fromDay,
        DateOnly toDay,
        CancellationToken cancellationToken);
}
