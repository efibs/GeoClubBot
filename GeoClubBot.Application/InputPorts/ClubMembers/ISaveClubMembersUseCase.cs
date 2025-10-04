using Entities;

namespace UseCases.InputPorts.ClubMembers;

public interface ISaveClubMembersUseCase
{
    Task SaveClubMembersAsync(IEnumerable<ClubMember> clubMembers);
}