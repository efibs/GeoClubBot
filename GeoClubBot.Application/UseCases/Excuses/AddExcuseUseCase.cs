using Entities;
using UseCases.InputPorts.ClubMembers;
using UseCases.InputPorts.Excuses;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Excuses;

public class AddExcuseUseCase(IReadOrSyncClubMemberUseCase readClubMemberUseCase, IUnitOfWork unitOfWork) : IAddExcuseUseCase
{
    public async Task<Guid?> AddExcuseAsync(string memberNickname, DateTimeOffset from, DateTimeOffset to)
    {
        // Try to read the club member
        var clubMember = await readClubMemberUseCase.ReadOrSyncClubMemberByNicknameAsync(memberNickname).ConfigureAwait(false);
        
        // If the club member could not be found
        if (clubMember == null)
        {
            return null;
        }
        
        // Build the new excuse
        var newExcuse = new ClubMemberExcuse
        {
            ExcuseId = Guid.NewGuid(),
            From = from,
            To = to,
            UserId = clubMember.UserId,
        };
        
        // Write the excuse
        var createdExcuse = unitOfWork.Excuses.CreateExcuse(newExcuse);
        
        // Save changes
        await unitOfWork.SaveChangesAsync().ConfigureAwait(false);
        
        return createdExcuse.ExcuseId;
    }
}