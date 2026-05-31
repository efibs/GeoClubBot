using Configuration;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;

namespace UseCases.UseCases.Club;

public sealed record GetClubByNameOrDefaultQuery(string? ClubName) : IQuery<Entities.Club?>;

public sealed record GetClubTodaysXpQuery(string? ClubName, bool IncludeWeeklies) : IQuery<GetClubTodaysXpResult>;

public sealed record GetClubTodaysXpResult(int? Xp, string? ClubName);

public sealed class ClubQueriesHandler(
    IClubRepository clubs,
    IGeoGuessrActivityReader activityReader,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig)
    : IRequestHandler<GetClubByNameOrDefaultQuery, Entities.Club?>,
      IRequestHandler<GetClubTodaysXpQuery, GetClubTodaysXpResult>
{
    private readonly Guid _defaultClubId = geoGuessrConfig.Value.MainClub.ClubId;

    public async Task<Entities.Club?> Handle(GetClubByNameOrDefaultQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ClubName))
        {
            return await clubs.ReadClubByIdAsync(_defaultClubId, cancellationToken).ConfigureAwait(false);
        }

        return await clubs.ReadClubByNameAsync(request.ClubName, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GetClubTodaysXpResult> Handle(GetClubTodaysXpQuery request, CancellationToken cancellationToken)
    {
        Entities.Club? club;
        if (string.IsNullOrWhiteSpace(request.ClubName))
        {
            club = await clubs.ReadClubByIdAsync(_defaultClubId, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            club = await clubs.ReadClubByNameAsync(request.ClubName, cancellationToken).ConfigureAwait(false);
        }

        if (club is null)
        {
            return new GetClubTodaysXpResult(null, null);
        }

        var activities = await activityReader
            .ReadTodaysActivitiesAsync(club.ClubId, cancellationToken)
            .ConfigureAwait(false);

        // Weekly missions are identified by the 1000 XP reward; everything else is a daily activity.
        const int weeklyMissionXpReward = 1000;
        var xp = activities
            .Where(a => request.IncludeWeeklies || a.XpReward != weeklyMissionXpReward)
            .Sum(a => a.XpReward);

        return new GetClubTodaysXpResult(xp, club.Name);
    }
}
