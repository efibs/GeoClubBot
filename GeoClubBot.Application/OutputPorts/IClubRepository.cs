using Entities;

namespace UseCases.OutputPorts;

public interface IClubRepository
{
    Club CreateClub(Club club);

    Task<Club> CreateOrUpdateClubAsync(Club club, CancellationToken cancellationToken = default);

    Task<Club?> ReadClubByIdAsync(Guid clubId, CancellationToken cancellationToken = default);

    Task<Club?> ReadForUpdateByIdAsync(Guid clubId, CancellationToken cancellationToken = default);

    Task<Club?> ReadClubByNameAsync(string clubName, CancellationToken cancellationToken = default);
}
