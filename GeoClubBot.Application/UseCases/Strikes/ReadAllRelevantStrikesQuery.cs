using Configuration;
using Entities;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;

namespace UseCases.UseCases.Strikes;

public sealed record ReadAllRelevantStrikesQuery : IQuery<List<ClubMemberRelevantStrike>>;

public sealed class ReadAllRelevantStrikesHandler(
    IGeoGuessrClientFactory geoGuessrClientFactory,
    IStrikesRepository strikes,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig)
    : MediatR.IRequestHandler<ReadAllRelevantStrikesQuery, List<ClubMemberRelevantStrike>>
{
    public async Task<List<ClubMemberRelevantStrike>> Handle(
        ReadAllRelevantStrikesQuery request, CancellationToken cancellationToken)
    {
        var relevantStrikes = new List<ClubMemberRelevantStrike>();

        foreach (var club in geoGuessrConfig.Value.Clubs)
        {
            var client = geoGuessrClientFactory.CreateClient(club.ClubId);

            var clubMembers = await client
                .ReadClubMembersAsync(club.ClubId, cancellationToken)
                .ConfigureAwait(false);

            // Batch-read the active strike counts for every member in one round-trip
            var userIds = clubMembers.Select(m => m.User.UserId).ToList();
            var activeStrikeCounts = await strikes
                .ReadActiveStrikeCountsByMemberUserIdsAsync(userIds, cancellationToken)
                .ConfigureAwait(false);

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
