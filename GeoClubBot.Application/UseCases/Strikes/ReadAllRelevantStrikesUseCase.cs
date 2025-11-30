using Constants;
using Entities;
using Microsoft.Extensions.Configuration;
using UseCases.InputPorts.Strikes;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;

namespace UseCases.UseCases.Strikes;

public class ReadAllRelevantStrikesUseCase(IGeoGuessrClient geoGuessrClient,
    IUnitOfWork unitOfWork,
    IConfiguration config) 
    : IReadAllRelevantStrikesUseCase
{
    public async Task<List<ClubMemberRelevantStrike>> ReadAllRelevantStrikesAsync()
    {
        // Read the current club members
        var clubMembers = await geoGuessrClient
            .ReadClubMembersAsync(_clubId)
            .ConfigureAwait(false);
        
        // Create the result list 
        var relevantStrikes = new List<ClubMemberRelevantStrike>();
        
        // For every club member 
        foreach (var clubMember in clubMembers)
        {
            // Read the number of active strikes for the user
            var numActiveStrikes = await unitOfWork.Strikes
                .ReadNumberOfActiveStrikesByMemberUserIdAsync(clubMember.User.UserId)
                .ConfigureAwait(false);
            
            // If the user doesn't have strikes
            if (numActiveStrikes is null or 0)
            {
                // No relevant strikes for him
                continue;
            }
            
            // Build the relevant strike object
            var relevantStrike = new ClubMemberRelevantStrike(clubMember.User.Nick, numActiveStrikes.Value);
            
            // Add to list
            relevantStrikes.Add(relevantStrike);
        }
        
        return relevantStrikes;
    }
    
    private readonly Guid _clubId = config.GetValue<Guid>(ConfigKeys.GeoGuessrClubIdConfigurationKey);
}