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
        return await _createClubMemberAsync(member).ConfigureAwait(false);
    }

    private async Task<ClubMember> _createClubMemberAsync(ClubMember clubMember)
    {
        // Create the club member
        var createdClubMember = await unitOfWork.ClubMembers.CreateClubMemberAsync(clubMember).ConfigureAwait(false);

        // If the user is in a club
        if (createdClubMember.ClubId is not null)
        {
            // Build the joined event
            var joinedEvent = new PlayerJoinedClubEvent(createdClubMember);

            // Add the event
            createdClubMember.AddDomainEvent(joinedEvent);
        }

        return createdClubMember;
    }

    private async Task<ClubMember?> _updateClubMemberAsync(ClubMember oldClubMember, ClubMember clubMember)
    {
        // Update the club member 
        var updatedClubMember = await unitOfWork.ClubMembers.UpdateClubMemberAsync(clubMember).ConfigureAwait(false);

        // If the club member was not found
        if (updatedClubMember is null)
        {
            return null;
        }
        
        // If the user is no longer in a club but was 
        if (oldClubMember.ClubId is not null && clubMember.ClubId is null)
        {
            // Build the left event
            var leftEvent = new PlayerLeftClubEvent(oldClubMember);
        
            // Add the domain event
            updatedClubMember.AddDomainEvent(leftEvent);
        }
        else if (oldClubMember.ClubId is null && clubMember.ClubId is not null)
        {
            // Else if the user was not in a club but is now
            
            // Build the joined event
            var joinedEvent = new PlayerJoinedClubEvent(updatedClubMember);
        
            // Add the domain event
            updatedClubMember.AddDomainEvent(joinedEvent);
        }
        else if (oldClubMember.ClubId is not null && clubMember.ClubId is not null && oldClubMember.ClubId != clubMember.ClubId)
        {
            // Else if both old and new club id are set and are not the same
            
            // Build the updated event
            var switchedEvent = new PlayerSwitchedClubsEvent(oldClubMember, updatedClubMember);
        
            // Add the domain event
            updatedClubMember.AddDomainEvent(switchedEvent);
        }
        
        return updatedClubMember;
    }
}