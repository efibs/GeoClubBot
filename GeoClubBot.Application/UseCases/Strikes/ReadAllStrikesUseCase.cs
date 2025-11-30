using Entities;
using UseCases.InputPorts.Strikes;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Strikes;

public class ReadAllStrikesUseCase(IUnitOfWork unitOfWork) : IReadAllStrikesUseCase
{
    public async Task<List<ClubMemberStrike>> ReadAllStrikesAsync()
    {
        // Read the strikes
        var strikes = await unitOfWork.Strikes.ReadAllStrikesAsync().ConfigureAwait(false);
        
        return strikes;
    }
}