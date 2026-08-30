using Configuration;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;

namespace UseCases.UseCases.Club;

public sealed record GetClubByNameOrDefaultQuery(string? ClubName) : IQuery<Entities.Club?>;

public sealed record GetClubTodaysXpQuery(string? ClubName, bool IncludeWeeklies) : IQuery<GetClubTodaysXpResult>;

/// <summary>
/// Today's club XP, plus how many members earned each of the two daily awards. They are counted
/// separately because a member can do the daily mission, the daily challenge, both, or neither.
/// </summary>
public sealed record GetClubTodaysXpResult(
    int? Xp,
    string? ClubName,
    int? MissionMemberCount,
    int? ChallengeMemberCount,
    int? TotalMemberCount);

public sealed record GetAllClubsQuery : IQuery<IReadOnlyList<Entities.Club>>;

public sealed class ClubQueriesHandler(
    IClubRepository clubs,
    IClubMemberRepository clubMembers,
    IGeoGuessrActivityReader activityReader,
    ClubActivityKindClassifier activityKinds,
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
            return new GetClubTodaysXpResult(null, null, null, null, null);
        }

        var activities = await activityReader
            .ReadTodaysActivitiesAsync(club.ClubId, cancellationToken)
            .ConfigureAwait(false);

        var relevantActivities = activities
            .Where(a => request.IncludeWeeklies || !activityKinds.IsWeeklyMission(a))
            .ToList();

        var xp = relevantActivities.Sum(a => a.XpReward);

        // Counted per award rather than "anyone with an activity today": the feed also carries
        // zero-XP entries (a club challenge being played), which say nothing about club XP.
        var missionMemberCount = DistinctMembers(activities, activityKinds.IsDailyMission);
        var challengeMemberCount = DistinctMembers(activities, activityKinds.IsDailyChallenge);

        var members = await clubMembers
            .ReadClubMembersByClubIdAsync(club.ClubId, cancellationToken)
            .ConfigureAwait(false);

        return new GetClubTodaysXpResult(xp, club.Name, missionMemberCount, challengeMemberCount, members.Count);
    }

    private static int DistinctMembers(
        IEnumerable<ReadClubActivitiesItemDto> activities,
        Func<ReadClubActivitiesItemDto, bool> predicate) =>
        activities.Where(predicate).Select(a => a.UserId).Distinct().Count();

    public Task<IReadOnlyList<Entities.Club>> Handle(GetAllClubsQuery request, CancellationToken cancellationToken) =>
        clubs.ReadAllClubsAsync(cancellationToken);
}
