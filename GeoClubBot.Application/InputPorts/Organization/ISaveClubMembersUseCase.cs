using UseCases.OutputPorts.GeoGuessr.DTOs;

namespace UseCases.InputPorts.Organization;

public interface ISaveClubMembersUseCase
{
    Task SaveClubMembersAsync(IEnumerable<GeoGuessrClubMemberDTO> clubMembers);
}