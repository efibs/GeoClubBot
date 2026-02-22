using Configuration;
using Entities;
using Microsoft.Extensions.Options;
using UseCases.InputPorts.Strikes;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;

namespace UseCases.UseCases.Strikes;

public class ReadAllRelevantStrikesUseCase(IGeoGuessrClientFactory geoGuessrClientFactory,
    IUnitOfWork unitOfWork,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig)
    : IReadAllRelevantStrikesUseCase
{
    public async Task<List<ClubMemberRelevantStrike>> ReadAllRelevantStrikesAsync()
    {
        // Create the result list
        var relevantStrikes = new List<ClubMemberRelevantStrike>();

        // Iterate all clubs
        foreach (var club in geoGuessrConfig.Value.Clubs)
        {
            // Get the client for this club
            var client = geoGuessrClientFactory.CreateClient(club.ClubId);

            // Read the current club members
            var clubMembers = await client
                .ReadClubMembersAsync(club.ClubId)
                .ConfigureAwait(false);

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
        }

        return relevantStrikes;
    }
}
