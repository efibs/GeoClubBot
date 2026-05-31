using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;
using UseCases.OutputPorts.GeoGuessr;

namespace UseCases.UseCases.Strikes;

public sealed record ReadAllRelevantStrikesQuery : IQuery<List<ClubMemberRelevantStrike>>;

public sealed class ReadAllRelevantStrikesHandler(
    IGeoGuessrClientFactory geoGuessrClientFactory,
    IServiceScopeFactory scopeFactory,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig)
    : IRequestHandler<ReadAllRelevantStrikesQuery, List<ClubMemberRelevantStrike>>
{
    public async Task<List<ClubMemberRelevantStrike>> Handle(
        ReadAllRelevantStrikesQuery request, CancellationToken cancellationToken)
    {
        // Each club fans out into its own DI scope so per-club IStrikesRepository reads
        // don't share a DbContext across parallel branches.
        var perClubStrikes = await Task.WhenAll(geoGuessrConfig.Value.Clubs.Select(async club =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var scopedStrikes = scope.ServiceProvider.GetRequiredService<IStrikesRepository>();

            var client = geoGuessrClientFactory.CreateClient(club.ClubId);
            var clubMembers = await client
                .ReadClubMembersAsync(club.ClubId, cancellationToken)
                .ConfigureAwait(false);

            var userIds = clubMembers.Select(m => m.User.UserId).ToList();
            var activeStrikeCounts = await scopedStrikes
                .ReadActiveStrikeCountsByMemberUserIdsAsync(userIds, cancellationToken)
                .ConfigureAwait(false);

            var clubStrikes = new List<ClubMemberRelevantStrike>();
            foreach (var clubMember in clubMembers)
            {
                if (!activeStrikeCounts.TryGetValue(clubMember.User.UserId, out var numActiveStrikes) || numActiveStrikes == 0)
                {
                    continue;
                }

                clubStrikes.Add(new ClubMemberRelevantStrike(clubMember.User.Nick, numActiveStrikes));
            }
            return clubStrikes;
        })).ConfigureAwait(false);

        return perClubStrikes.SelectMany(s => s).ToList();
    }
}
