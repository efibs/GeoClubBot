using Entities;

namespace UseCases.InputPorts;

public interface IReadOrSyncClubUseCase
{
    Task<Club> ReadOrSyncClubAsync();
}