using MediatR;
using UseCases.UseCases.Club;

namespace UseCases.UseCases.ClubMemberActivity;

public sealed class ActivityLeaderboardHandler(ISender mediator)
    : IRequestHandler<GetActivityLeaderboardQuery, GetActivityLeaderboardResult>
{
    public async Task<GetActivityLeaderboardResult> Handle(GetActivityLeaderboardQuery request, CancellationToken cancellationToken)
    {
        var club = await mediator
            .Send(new GetClubByNameOrDefaultQuery(request.ClubName), cancellationToken)
            .ConfigureAwait(false);

        if (club is null)
        {
            return new GetActivityLeaderboardResult(null, null);
        }

        var leaderboard = await mediator
            .Send(new CalculateAverageXpQuery(club.ClubId, request.HistoryDepth), cancellationToken)
            .ConfigureAwait(false);

        var topMembers = leaderboard
            .OrderByDescending(m => m.AverageXp)
            .ThenBy(m => m.JoinedAt)
            .ToList();

        return new GetActivityLeaderboardResult(topMembers, club.Name);
    }
}
