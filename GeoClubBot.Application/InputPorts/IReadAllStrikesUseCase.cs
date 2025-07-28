using Entities;

namespace UseCases.InputPorts;

public interface IReadAllStrikesUseCase
{
    Task<List<ClubMemberStrike>> ReadAllStrikesAsync();
}