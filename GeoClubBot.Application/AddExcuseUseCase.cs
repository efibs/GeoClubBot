using Entities;
using UseCases.InputPorts;
using UseCases.OutputPorts;

namespace UseCases;

public class AddExcuseUseCase(IReadOrSyncClubMemberUseCase readClubMemberUseCase, IExcusesRepository excusesRepository) : IAddExcuseUseCase
{
    public async Task<Guid?> AddExcuseAsync(string memberNickname, DateTimeOffset from, DateTimeOffset to)
    {
        // Try to read the club member
        var clubMember = await readClubMemberUseCase.ReadOrSyncClubMemberByNicknameAsync(memberNickname);
        
        // If the club member could not be found
        if (clubMember == null)
        {
            return null;
        }
        
        // Build the new excuse
        var newExcuse = new ClubMemberExcuse(Guid.NewGuid(), clubMember.UserId, from, to);
        
        // Write the excuse
        var createdExcuse = await excusesRepository.CreateExcuseAsync(newExcuse);
        
        return createdExcuse?.Id;
    }
}