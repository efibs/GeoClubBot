using Entities;

namespace UseCases.InputPorts.Strikes;

public interface IReadAllStrikesUseCase
{
    Task<List<ClubMemberStrike>> ReadAllStrikesAsync();
}