using Entities;

namespace UseCases.OutputPorts.Repositories;

public interface IClubRepository
{
    Club CreateClub(Club club);

    Task<Club> CreateOrUpdateClubAsync(Club club, CancellationToken cancellationToken = default);

    Task<Club?> ReadClubByIdAsync(Guid clubId, CancellationToken cancellationToken = default);

    Task<Club?> ReadForUpdateByIdAsync(Guid clubId, CancellationToken cancellationToken = default);

    Task<Club?> ReadClubByNameAsync(string clubName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Club>> ReadAllClubsAsync(CancellationToken cancellationToken = default);
}
