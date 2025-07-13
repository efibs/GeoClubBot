using Entities;

namespace UseCases.OutputPorts;

public interface IGeoGuessrAccess
{
    Task<List<GeoGuessrClubMember>> ReadClubMembersAsync(Guid clubId);
    
    Task<GeoGuessrClub> ReadClubAsync(Guid clubId);
}