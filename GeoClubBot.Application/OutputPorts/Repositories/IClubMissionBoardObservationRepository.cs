using Entities;

namespace UseCases.OutputPorts.Repositories;

public interface IClubMissionBoardObservationRepository
{
    /// <summary>The club's last observed board state, tracked for changes; null before the first.</summary>
    Task<ClubMissionBoardObservation?> ReadForUpdateAsync(Guid clubId, CancellationToken cancellationToken = default);

    void Add(ClubMissionBoardObservation observation);
}
