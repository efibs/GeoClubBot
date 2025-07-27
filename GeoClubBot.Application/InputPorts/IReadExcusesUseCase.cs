using Entities;

namespace UseCases.InputPorts;

public interface IReadExcusesUseCase
{
    Task<List<ClubMemberExcuse>> ReadExcusesAsync(string memberNickname);
    
    Task<List<ClubMemberExcuse>> ReadExcusesAsync();
}