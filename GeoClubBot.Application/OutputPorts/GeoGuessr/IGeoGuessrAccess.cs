using Entities;

namespace UseCases.OutputPorts.GeoGuessr;

public interface IGeoGuessrAccess
{
    Task<List<GeoGuessrClubMemberDTO>> ReadClubMembersAsync(Guid clubId);
    
    Task<GeoGuessrClubDTO> ReadClubAsync(Guid clubId);
}