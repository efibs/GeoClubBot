using Entities;

namespace UseCases.InputPorts.MemberPrivateChannels;

public interface IDeleteMemberPrivateChannelUseCase
{
    Task<bool> DeletePrivateChannelAsync(ClubMember? clubMember);
}