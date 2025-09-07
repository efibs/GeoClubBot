using Entities;
using UseCases.InputPorts.Excuses;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Excuses;

public class ReadExcusesUseCase(IExcusesRepository excusesRepository) : IReadExcusesUseCase
{
    public async Task<List<ClubMemberExcuse>> ReadExcusesAsync(string memberNickname)
    {
        // Read the excuses
        var excuses = await excusesRepository.ReadExcusesByMemberNicknameAsync(memberNickname);
        
        return excuses;
    }
    
    public async Task<List<ClubMemberExcuse>> ReadExcusesAsync()
    {
        // Read the excuses
        var excuses = await excusesRepository.ReadExcusesAsync();
        
        return excuses;
    }
}