using Entities;

namespace UseCases.InputPorts.MemberPrivateChannels;

public interface ICreateMemberPrivateChannelUseCase
{
    Task<ulong?> CreatePrivateChannelAsync(ClubMember clubMember);
}