using Entities;
using UseCases.InputPorts.Excuses;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Excuses;

public class ReadExcusesUseCase(IUnitOfWork unitOfWork) : IReadExcusesUseCase
{
    public async Task<List<ClubMemberExcuse>> ReadExcusesAsync(string memberNickname)
    {
        // Read the excuses
        var excuses = await unitOfWork.Excuses.ReadExcusesByMemberNicknameAsync(memberNickname).ConfigureAwait(false);
        
        return excuses;
    }
    
    public async Task<List<ClubMemberExcuse>> ReadExcusesAsync()
    {
        // Read the excuses
        var excuses = await unitOfWork.Excuses.ReadExcusesAsync().ConfigureAwait(false);
        
        return excuses;
    }
}