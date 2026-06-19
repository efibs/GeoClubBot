using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.GeoGuessr.Assemblers;
using UseCases.OutputPorts.Repositories;

namespace UseCases.UseCases.DailyChallenge;

/// <summary>
/// Reads the live highscores of the club's currently-active daily challenges. The challenge results
/// themselves are not persisted (only the links are), so standings are fetched fresh from GeoGuessr —
/// which makes this a natural "live" panel for the Club Dashboard Activity.
/// </summary>
public sealed record GetCurrentChallengeResultsQuery : IQuery<List<ClubChallengeResult>>;

public sealed class GetCurrentChallengeResultsHandler(
    IGeoGuessrClientFactory geoGuessrClientFactory,
    IClubChallengeRepository clubChallenges,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig)
    : IRequestHandler<GetCurrentChallengeResultsQuery, List<ClubChallengeResult>>
{
    public async Task<List<ClubChallengeResult>> Handle(GetCurrentChallengeResultsQuery request, CancellationToken cancellationToken)
    {
        var links = await clubChallenges.ReadLatestClubChallengeLinksAsync(cancellationToken).ConfigureAwait(false);
        if (links.Count == 0)
        {
            return [];
        }

        // Challenges are always created on behalf of the main club's account (see DailyChallengeHandler).
        var client = geoGuessrClientFactory.CreateClient(geoGuessrConfig.Value.MainClub.ClubId);

        var results = new List<ClubChallengeResult>(links.Count);
        foreach (var link in links.OrderBy(l => l.RolePriority))
        {
            var queryParams = new ReadHighscoresQueryParams { Limit = 10, MinRounds = 5 };
            var response = await client
                .ReadHighscoresAsync(link.ChallengeId, queryParams, cancellationToken)
                .ConfigureAwait(false);

            var players = ChallengeResultHighScoresAssembler.AssembleEntities(response);
            results.Add(new ClubChallengeResult(link.Difficulty, link.RolePriority, players));
        }

        return results;
    }
}
