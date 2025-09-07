using Entities;

namespace UseCases.InputPorts.Strikes;

public interface IReadMemberStrikesUseCase
{
    Task<ClubMemberStrikeStatus?> ReadMemberStrikesAsync(string memberNickname);
}