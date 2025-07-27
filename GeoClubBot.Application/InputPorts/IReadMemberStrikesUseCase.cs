using Entities;

namespace UseCases.InputPorts;

public interface IReadMemberStrikesUseCase
{
    Task<ClubMemberStrikeStatus?> ReadMemberStrikesAsync(string memberNickname);
}