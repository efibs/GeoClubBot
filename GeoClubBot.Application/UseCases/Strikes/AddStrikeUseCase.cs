using Entities;
using UseCases.InputPorts.ClubMembers;
using UseCases.InputPorts.Strikes;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Strikes;

public class AddStrikeUseCase(IReadOrSyncClubMemberUseCase readClubMemberUseCase, IUnitOfWork unitOfWork) : IAddStrikeUseCase
{
    public async Task<Guid?> AddStrikeAsync(string memberNickname, DateTimeOffset strikeDate)
    {
        // Try to read the club member
        var clubMember = await readClubMemberUseCase.ReadOrSyncClubMemberByNicknameAsync(memberNickname).ConfigureAwait(false);
        
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
        var createdStrike = unitOfWork.Strikes.CreateStrike(newStrike);
        
        // Save
        await unitOfWork.SaveChangesAsync().ConfigureAwait(false);
        
        return createdStrike?.StrikeId;
    }
}