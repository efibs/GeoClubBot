using Entities;
using MediatR;
using UseCases.InputPorts.ClubMembers;
using UseCases.OutputPorts;

namespace UseCases.UseCases.ClubMembers;

public class CreateOrUpdateClubMemberUseCase(IPublisher publisher, IClubMemberRepository repository) : ICreateOrUpdateClubMemberUseCase
{
    public async Task<ClubMember?> CreateOrUpdateClubMemberAsync(ClubMember member)
    {
        // Try to read the club member
        var existingClubMember = await repository.ReadClubMemberByUserIdAsync(member.UserId).ConfigureAwait(false);
        
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
        var createdClubMember = await repository.CreateClubMemberAsync(clubMember).ConfigureAwait(false);
        
        // Build the created event
        var createdEvent = new ClubMemberCreatedEvent(createdClubMember);
        
        // Publish the event
        await publisher.Publish(createdEvent).ConfigureAwait(false);

        return createdClubMember;
    }

    private async Task<ClubMember?> _updateClubMemberAsync(ClubMember oldClubMember, ClubMember clubMember)
    {
        // Update the club member 
        var updatedClubMember = await repository.UpdateClubMemberAsync(clubMember).ConfigureAwait(false);
        
        // If the club member could not be updated
        if (updatedClubMember == null)
        {
            return null;
        }
        
        // Build the updated event
        var updatedEvent = new ClubMemberUpdatedEvent(oldClubMember, updatedClubMember);
        
        // Publish the event
        await publisher.Publish(updatedEvent).ConfigureAwait(false);
        
        return updatedClubMember;
    }
}