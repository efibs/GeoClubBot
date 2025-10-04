using Entities;

namespace UseCases.InputPorts.ClubMembers;

public interface ICreateOrUpdateClubMemberUseCase
{
    Task<ClubMember?> CreateOrUpdateClubMemberAsync(ClubMember member);
}