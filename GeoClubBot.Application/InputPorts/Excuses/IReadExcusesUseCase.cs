using Entities;

namespace UseCases.InputPorts.Excuses;

public interface IReadExcusesUseCase
{
    Task<List<ClubMemberExcuse>> ReadExcusesAsync(string memberNickname);
    
    Task<List<ClubMemberExcuse>> ReadExcusesAsync();
}