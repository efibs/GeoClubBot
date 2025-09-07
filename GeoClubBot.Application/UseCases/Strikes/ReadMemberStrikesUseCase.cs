using Entities;
using UseCases.InputPorts.Strikes;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Strikes;

public class ReadMemberStrikesUseCase(IStrikesRepository strikesRepository) : IReadMemberStrikesUseCase
{
    public async Task<ClubMemberStrikeStatus?> ReadMemberStrikesAsync(string memberNickname)
    {
        // Read the number of strikes
        var strikes = await strikesRepository.ReadStrikesByMemberNicknameAsync(memberNickname);

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