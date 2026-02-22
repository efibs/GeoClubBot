using Entities;

namespace UseCases.InputPorts.ClubMemberActivity;

public interface ICalculateAverageXpUseCase
{
    Task<List<ClubMemberAverageXp>> CalculateAverageXpAsync(Guid clubId, int historyDepth);
}
