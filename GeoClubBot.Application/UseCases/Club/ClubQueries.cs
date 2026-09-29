using Configuration;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;

namespace UseCases.UseCases.Club;

public sealed record GetClubByNameOrDefaultQuery(string? ClubName) : IQuery<Entities.Club?>;

public sealed record GetClubTodaysXpQuery(string? ClubName) : IQuery<GetClubTodaysXpResult>;

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
