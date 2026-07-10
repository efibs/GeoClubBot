using Configuration;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;

namespace UseCases.UseCases.Club;

public sealed record GetClubByNameOrDefaultQuery(string? ClubName) : IQuery<Entities.Club?>;

public sealed record GetClubTodaysXpQuery(string? ClubName, bool IncludeWeeklies) : IQuery<GetClubTodaysXpResult>;

public sealed record GetClubTodaysXpResult(int? Xp, string? ClubName, int? CompletedMemberCount, int? TotalMemberCount);

public sealed record GetAllClubsQuery : IQuery<IReadOnlyList<Entities.Club>>;

public sealed class ClubQueriesHandler(
    IClubRepository clubs,
    IClubMemberRepository clubMembers,
    IGeoGuessrActivityReader activityReader,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig)
    : IRequestHandler<GetClubByNameOrDefaultQuery, Entities.Club?>,
      IRequestHandler<GetClubTodaysXpQuery, GetClubTodaysXpResult>,
      IRequestHandler<GetAllClubsQuery, IReadOnlyList<Entities.Club>>
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
            return new GetClubTodaysXpResult(null, null, null, null);
        }

        var activities = await activityReader
            .ReadTodaysActivitiesAsync(club.ClubId, cancellationToken)
            .ConfigureAwait(false);

        // Weekly missions are identified by the 1000 XP reward; everything else is a daily activity.
        const int weeklyMissionXpReward = 1000;
        var relevantActivities = activities
            .Where(a => request.IncludeWeeklies || a.XpReward != weeklyMissionXpReward)
            .ToList();

        var xp = relevantActivities.Sum(a => a.XpReward);
        var completedMemberCount = relevantActivities.Select(a => a.UserId).Distinct().Count();

        var members = await clubMembers
            .ReadClubMembersByClubIdAsync(club.ClubId, cancellationToken)
            .ConfigureAwait(false);

        return new GetClubTodaysXpResult(xp, club.Name, completedMemberCount, members.Count);
    }

    public Task<IReadOnlyList<Entities.Club>> Handle(GetAllClubsQuery request, CancellationToken cancellationToken) =>
        clubs.ReadAllClubsAsync(cancellationToken);
}
