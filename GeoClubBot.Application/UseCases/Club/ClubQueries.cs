using Configuration;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;

namespace UseCases.UseCases.Club;

/// <summary>The club with that id, or the main club when no id is given.</summary>
public sealed record GetClubByIdOrDefaultQuery(Guid? ClubId) : IQuery<Entities.Club?>;

/// <param name="ClubId">The club; the main club when null.</param>
public sealed record GetClubTodaysXpQuery(Guid? ClubId) : IQuery<GetClubTodaysXpResult>;

/// <summary>
/// Today's (UTC) club XP, how many members kept their streak, how many board missions were
/// finished, and how many members claimed a mission in the current claim cycle.
/// </summary>
/// <param name="ClaimMemberCount">Members with a claim this claim cycle; null when the board can't be read.</param>
public sealed record GetClubTodaysXpResult(
    int? Xp,
    string? ClubName,
    int? ChallengeMemberCount,
    int? BoardMissionCount,
    int? ClaimMemberCount,
    int? TotalMemberCount);

public sealed record GetAllClubsQuery : IQuery<IReadOnlyList<Entities.Club>>;

public sealed class ClubQueriesHandler(
    IClubRepository clubs,
    IClubMemberRepository clubMembers,
    IGeoGuessrActivityReader activityReader,
    IClubMissionBoardReader boardReader,
    ClubActivityKindClassifier activityKinds,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    IOptions<MissionBoardConfiguration> missionBoardConfig)
    : IRequestHandler<GetClubByIdOrDefaultQuery, Entities.Club?>,
      IRequestHandler<GetClubTodaysXpQuery, GetClubTodaysXpResult>,
      IRequestHandler<GetAllClubsQuery, IReadOnlyList<Entities.Club>>
{
    private readonly Guid _defaultClubId = geoGuessrConfig.Value.MainClub.ClubId;

    public Task<Entities.Club?> Handle(GetClubByIdOrDefaultQuery request, CancellationToken cancellationToken) =>
        clubs.ReadClubByIdAsync(request.ClubId ?? _defaultClubId, cancellationToken);

    public async Task<GetClubTodaysXpResult> Handle(GetClubTodaysXpQuery request, CancellationToken cancellationToken)
    {
        var club = await clubs
            .ReadClubByIdAsync(request.ClubId ?? _defaultClubId, cancellationToken)
            .ConfigureAwait(false);

        if (club is null)
        {
            return new GetClubTodaysXpResult(null, null, null, null, null, null);
        }

        var activities = await activityReader
            .ReadTodaysActivitiesAsync(club.ClubId, cancellationToken)
            .ConfigureAwait(false);

        var xp = activities.Sum(a => a.XpReward);

        // Counted per member rather than "anyone with an activity today": the feed also carries
        // zero-XP entries (a club challenge being played), which say nothing about club XP.
        var challengeMemberCount = activities
            .Where(activityKinds.IsDailyChallenge)
            .Select(a => a.UserId)
            .Distinct()
            .Count();
        var boardMissionCount = activities.Count(activityKinds.IsBoardMission);

        var board = await boardReader.ReadCurrentAsync(club.ClubId, cancellationToken).ConfigureAwait(false);
        int? claimMemberCount = null;
        if (board is not null)
        {
            var cycleStart = board.ClaimCycleStart(DateTimeOffset.UtcNow, missionBoardConfig.Value.FallbackClaimCycleStart);
            claimMemberCount = board.AllTiles
                .Where(t => t.ClaimedBy is not null && t.ClaimedAt >= cycleStart)
                .Select(t => t.ClaimedBy)
                .Distinct()
                .Count();
        }

        var members = await clubMembers
            .ReadClubMembersByClubIdAsync(club.ClubId, cancellationToken)
            .ConfigureAwait(false);

        return new GetClubTodaysXpResult(
            xp, club.Name, challengeMemberCount, boardMissionCount, claimMemberCount, members.Count);
    }

    public Task<IReadOnlyList<Entities.Club>> Handle(GetAllClubsQuery request, CancellationToken cancellationToken) =>
        clubs.ReadAllClubsAsync(cancellationToken);
}
