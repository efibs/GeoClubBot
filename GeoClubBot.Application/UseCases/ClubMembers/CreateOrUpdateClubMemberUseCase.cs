using Entities;
using UseCases.InputPorts.ClubMembers;
using UseCases.OutputPorts;

namespace UseCases.UseCases.ClubMembers;

public class CreateOrUpdateClubMemberUseCase(IUnitOfWork unitOfWork) : ICreateOrUpdateClubMemberUseCase
{
    public async Task<ClubMember?> CreateOrUpdateClubMemberAsync(ClubMember member)
    {
        // Try to read the club member
        var existingClubMember = await unitOfWork.ClubMembers.ReadClubMemberByUserIdAsync(member.UserId).ConfigureAwait(false);
        
        // If there is a club member
        if (existingClubMember != null)
        {
            // Update the member
            return await _updateClubMemberAsync(existingClubMember, member).ConfigureAwait(false);
        }

        // Create the member
        return _createClubMember(member);
    }

    private ClubMember _createClubMember(ClubMember clubMember)
    {
        // Build the created event
        var createdEvent = new ClubMemberCreatedEvent(clubMember);
        
        // Add the event
        clubMember.AddDomainEvent(createdEvent);
        
        // Create the club member
        var createdClubMember = unitOfWork.ClubMembers.CreateClubMember(clubMember);
 
        return createdClubMember;
    }

    private async Task<ClubMember?> _updateClubMemberAsync(ClubMember oldClubMember, ClubMember clubMember)
    {
        // Build the updated event
        var updatedEvent = new ClubMemberUpdatedEvent(oldClubMember, clubMember);
        
        // Add the domain event
        clubMember.AddDomainEvent(updatedEvent);
        
        // Update the club member 
        var updatedClubMember = await unitOfWork.ClubMembers.UpdateClubMemberAsync(clubMember).ConfigureAwait(false);

        return updatedClubMember;
    }
}