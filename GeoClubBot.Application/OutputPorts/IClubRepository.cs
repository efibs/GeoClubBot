using Entities;

namespace UseCases.OutputPorts;

public interface IClubRepository
{
    Club CreateClub(Club club);
    
    Task<Club> CreateOrUpdateClubAsync(Club club);
    
    Task<Club?> ReadClubByIdAsync(Guid clubId);
}