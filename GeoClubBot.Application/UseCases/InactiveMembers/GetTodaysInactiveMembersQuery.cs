using Configuration;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.InactiveMembers;

/// <summary>
/// The club members who have not been active today yet, reported as two independent lists: who
/// hasn't played the daily challenge or a duel today (UTC), and who could claim a board mission in
/// the current claim cycle but hasn't. A member can be on either, both, or neither list. A
/// <c>null</c> <paramref name="ClubId"/> targets the configured main club.
/// </summary>
public sealed record GetTodaysInactiveMembersQuery(Guid? ClubId)
    : IQuery<Result<TodaysInactiveMembers>>;

/// <param name="ClaimInactive">
/// Members who haven't claimed a mission this claim cycle although missions are free (and who
/// hold no open mission); null when the board can't be read.
/// </param>
/// <param name="FreeMissions">Free missions on the current board; null when the board can't be read.</param>
public sealed record TodaysInactiveMembers(
    Guid ClubId,
    string ClubName,
    DateOnly Day,
    int TotalMembers,
    IReadOnlyList<InactiveMember> ChallengeInactive,
    IReadOnlyList<InactiveMember>? ClaimInactive,
    int? FreeMissions = null);

/// <summary>
/// An inactive member, named by their GeoGuessr <paramref name="Nickname"/> plus, when the account
/// is linked, their <paramref name="DiscordUserId"/> so the presentation layer can mention them.
/// </summary>
public sealed record InactiveMember(string Nickname, ulong? DiscordUserId);

public sealed class GetTodaysInactiveMembersHandler(
    IClubMemberRepository clubMembers,
    IClubRepository clubs,
    IGeoGuessrActivityReader activityReader,
    IClubMissionBoardReader boardReader,
    ClubActivityKindClassifier activityKinds,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    IOptions<MissionBoardConfiguration> missionBoardConfig)
    : IRequestHandler<GetTodaysInactiveMembersQuery, Result<TodaysInactiveMembers>>
{
    public async Task<Result<TodaysInactiveMembers>> Handle(
        GetTodaysInactiveMembersQuery request,
        CancellationToken cancellationToken)
    {
        var clubId = request.ClubId ?? geoGuessrConfig.Value.MainClub.ClubId;

        // Only clubs the bot is configured for can be queried: the activity feed is fetched with a
        // configured token and the roster is only synced for those clubs.
        if (geoGuessrConfig.Value.Clubs.All(c => c.ClubId != clubId))
        {
            return Error.NotFound("club.not_found", "The selected club is not tracked by the bot.");
        }

        // Both reads are cached (activity feed 5 min, board 1 min), so repeated admin invocations
        // don't re-hit the GeoGuessr API.
        var todaysActivities = await activityReader
            .ReadTodaysActivitiesAsync(clubId, cancellationToken)
            .ConfigureAwait(false);

        var challengeDoneUserIds = todaysActivities
            .Where(activityKinds.IsDailyChallenge)
            .Select(a => a.UserId)
            .ToHashSet();

        var members = await clubMembers
            .ReadClubMembersByClubIdAsync(clubId, cancellationToken)
            .ConfigureAwait(false);

        var club = await clubs.ReadClubByIdAsync(clubId, cancellationToken).ConfigureAwait(false);
        var clubName = club?.Name ?? clubId.ToString();

        var board = await boardReader.ReadCurrentAsync(clubId, cancellationToken).ConfigureAwait(false);
        List<InactiveMember>? claimInactive = null;
        if (board is not null)
        {
            var cycleStart = board.ClaimCycleStart(DateTimeOffset.UtcNow, missionBoardConfig.Value.FallbackClaimCycleStart);
            claimInactive = BuildList(members, m =>
                board.ClaimStateOf(m.UserId, cycleStart) == Entities.ClubMissionClaimState.ClaimAvailable);
        }

        return new TodaysInactiveMembers(
            clubId,
            clubName,
            DateOnly.FromDateTime(DateTime.UtcNow),
            members.Count,
            BuildList(members, m => !challengeDoneUserIds.Contains(m.UserId)),
            claimInactive,
            board?.FreeTiles.Count);
    }

    private static List<InactiveMember> BuildList(
        IReadOnlyList<Entities.ClubMember> members,
        Func<Entities.ClubMember, bool> isInactive) =>
        members
            .Where(isInactive)
            .Select(m => new InactiveMember(m.User.Nickname, m.User.DiscordUserId))
            .OrderBy(m => m.Nickname, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
