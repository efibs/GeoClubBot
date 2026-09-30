using Configuration;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.CountryChallenges;

/// <summary>The current season's leaderboard, and where the asking member stands on it.</summary>
public sealed record GetCountryChallengeLeaderboardQuery(ulong? ViewerDiscordUserId = null)
    : IQuery<Result<CountryChallengeLeaderboard>>;

/// <param name="Top">The places the posted leaderboard shows, ties included.</param>
/// <param name="Viewer">The viewer's own line; null when they have no points or no linked account.</param>
/// <param name="ViewerLinked">Whether the viewer's Discord account is linked to a GeoGuessr account.</param>
public sealed record CountryChallengeLeaderboard(
    string Season,
    IReadOnlyList<CountryChallengeStanding> Top,
    CountryChallengeStanding? Viewer,
    bool ViewerLinked);

public sealed class GetCountryChallengeLeaderboardHandler(
    ISender mediator,
    ICountryChallengeRepository repository,
    IGeoGuessrUserRepository users,
    IOptions<CountryChallengesConfiguration> options)
    : IRequestHandler<GetCountryChallengeLeaderboardQuery, Result<CountryChallengeLeaderboard>>
{
    public async Task<Result<CountryChallengeLeaderboard>> Handle(
        GetCountryChallengeLeaderboardQuery request,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return Error.Validation(RunCountryChallengesHandler.DisabledCode, "The country challenges are not running on this server.");
        }

        var planResult = await mediator.Send(new LoadCountryChallengePlanQuery(), cancellationToken).ConfigureAwait(false);
        if (planResult.IsFailure)
        {
            return planResult.Error;
        }

        var leaderboard = planResult.Value.Leaderboard;
        var awards = await repository.ReadAwardsAsync(leaderboard.Season, cancellationToken).ConfigureAwait(false);
        var standings = LeaderboardRanking.Rank(awards);

        CountryChallengeStanding? viewer = null;
        var viewerLinked = false;
        if (request.ViewerDiscordUserId is { } discordUserId)
        {
            var user = await users.ReadUserByDiscordUserIdAsync(discordUserId, cancellationToken).ConfigureAwait(false);
            viewerLinked = user is not null;
            viewer = user is null ? null : standings.FirstOrDefault(s => s.UserId == user.UserId);
        }

        return new CountryChallengeLeaderboard(
            leaderboard.Season,
            [.. standings.Where(s => s.Rank <= leaderboard.Top)],
            viewer,
            viewerLinked);
    }
}
