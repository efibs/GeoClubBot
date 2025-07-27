using Entities;
using UseCases.InputPorts;
using UseCases.OutputPorts;

namespace UseCases;

public class AddStrikeUseCase(IReadOrSyncClubMemberUseCase readClubMemberUseCase, IStrikesRepository strikesRepository) : IAddStrikeUseCase
{
    public async Task<Guid?> AddStrikeAsync(string memberNickname, DateTimeOffset strikeDate)
    {
        // Try to read the club member
        var clubMember = await readClubMemberUseCase.ReadOrSyncClubMemberByNicknameAsync(memberNickname);
        
        // If the club member could not be found
        if (clubMember == null)
        {
            return null;
        }
        
        // Build the new strike 
        var newStrike = new ClubMemberStrike
        {
            StrikeId = Guid.NewGuid(),
            Revoked = false,
            Timestamp = strikeDate,
            UserId = clubMember.UserId
        };
        
        // Write the strike
        var createdStrike = await strikesRepository.CreateStrikeAsync(newStrike);
        
        return createdStrike?.StrikeId;
    }
}