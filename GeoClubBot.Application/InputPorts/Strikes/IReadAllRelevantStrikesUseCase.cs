using Entities;

namespace UseCases.InputPorts.Strikes;

public interface IReadAllRelevantStrikesUseCase
{
    Task<List<ClubMemberRelevantStrike>> ReadAllRelevantStrikesAsync();
}