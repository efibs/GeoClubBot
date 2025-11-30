using Entities;
using UseCases.InputPorts.Strikes;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Strikes;

public class ReadMemberStrikesUseCase(IUnitOfWork unitOfWork) : IReadMemberStrikesUseCase
{
    public async Task<ClubMemberStrikeStatus?> ReadMemberStrikesAsync(string memberNickname)
    {
        // Read the number of strikes
        var strikes = await unitOfWork.Strikes
            .ReadStrikesByMemberNicknameAsync(memberNickname)
            .ConfigureAwait(false);

        // If the player does not exist
        if (strikes == null)
        {
            return null;
        }
        
        // Count the active strikes
        var numActiveStrikes = strikes.Count(s => s.Revoked == false);
        
        return new ClubMemberStrikeStatus(numActiveStrikes, strikes);
    }
}