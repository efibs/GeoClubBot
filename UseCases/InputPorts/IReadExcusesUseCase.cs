using Entities;

namespace UseCases.InputPorts;

public interface IReadExcusesUseCase
{
    Task<List<GeoGuessrClubMemberExcuse>> ReadExcusesAsync(string memberNickname);
}