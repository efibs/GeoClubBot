using Entities;

namespace UseCases.OutputPorts;

public interface IClubRepository
{
    Task<Club?> CreateClubAsync(Club club);
    
    Task<Club?> ReadClubByIdAsync(Guid clubId);
}