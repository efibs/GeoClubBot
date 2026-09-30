using Entities;

namespace UseCases.OutputPorts.Repositories;

public interface IClubMemberDailyActivityRepository
{
    void AddRange(IEnumerable<ClubMemberDailyActivity> activities);

    Task<bool> HasSnapshotForDayAsync(Guid clubId, DateOnly day, CancellationToken cancellationToken);

    /// <summary>
    /// Reads all per-member rows whose <see cref="ClubMemberDailyActivity.Date"/> lies in
    /// [<paramref name="fromDay"/>, <paramref name="toDay"/>].
    /// A <c>null</c> <paramref name="clubId"/> reads across all clubs.
    /// </summary>
    Task<IReadOnlyList<ClubMemberDailyActivity>> ReadDailyActivitiesAsync(
        Guid? clubId,
        DateOnly fromDay,
        DateOnly toDay,
        CancellationToken cancellationToken);
}
