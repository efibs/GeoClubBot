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

            // Batch-read the active strike counts for every member in one round-trip
            var userIds = clubMembers.Select(m => m.User.UserId).ToList();
            var activeStrikeCounts = await unitOfWork.Strikes
                .ReadActiveStrikeCountsByMemberUserIdsAsync(userIds)
                .ConfigureAwait(false);

            // Zip strike counts back to members
            foreach (var clubMember in clubMembers)
            {
                if (!activeStrikeCounts.TryGetValue(clubMember.User.UserId, out var numActiveStrikes) || numActiveStrikes == 0)
                {
                    continue;
                }

                relevantStrikes.Add(new ClubMemberRelevantStrike(clubMember.User.Nick, numActiveStrikes));
            }
        }

        return relevantStrikes;
    }
}
