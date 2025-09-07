using Entities;
using UseCases.InputPorts.Excuses;
using UseCases.InputPorts.Organization;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Excuses;

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
        var newExcuse = new ClubMemberExcuse
        {
            ExcuseId = Guid.NewGuid(),
            From = from,
            To = to,
            UserId = clubMember.UserId,
        };
        
        // Write the excuse
        var createdExcuse = await excusesRepository.CreateExcuseAsync(newExcuse);
        
        return createdExcuse?.ExcuseId;
    }
}