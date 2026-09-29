using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.MissionBoard;

/// <summary>
/// A club's running mission board week, with the nicknames of everyone on it. A <c>null</c>
/// <paramref name="ClubId"/> targets the configured main club.
/// </summary>
public sealed record GetClubMissionBoardQuery(Guid? ClubId) : IQuery<Result<ClubMissionBoardView>>;

/// <param name="Nicknames">GeoGuessr user id → nickname of claimers and helpers the bot knows.</param>
/// <param name="ClaimCycleStart">Start of the current daily claim cycle.</param>
public sealed record ClubMissionBoardView(
    Guid ClubId,
    string ClubName,
    ClubMissionBoardWeek Board,
    IReadOnlyDictionary<string, string> Nicknames,
    DateTimeOffset ClaimCycleStart)
{
    public string NicknameOf(string userId) => Nicknames.GetValueOrDefault(userId) ?? userId;
}

public sealed class GetClubMissionBoardHandler(
    IClubMissionBoardReader boardReader,
    IClubRepository clubs,
    IClubMemberRepository clubMembers,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    IOptions<MissionBoardConfiguration> missionBoardConfig)
    : IRequestHandler<GetClubMissionBoardQuery, Result<ClubMissionBoardView>>
{
    public async Task<Result<ClubMissionBoardView>> Handle(GetClubMissionBoardQuery request, CancellationToken cancellationToken)
    {
        var clubId = request.ClubId ?? geoGuessrConfig.Value.MainClub.ClubId;

        // The board is read with the club's own token, so only configured clubs have one.
        if (geoGuessrConfig.Value.Clubs.All(c => c.ClubId != clubId))
        {
            return Error.NotFound("club.not_found", "The selected club is not tracked by the bot.");
        }

        var board = await boardReader.ReadCurrentAsync(clubId, cancellationToken).ConfigureAwait(false);
        if (board is null)
        {
            return Error.NotFound("mission_board.unavailable", "The club's mission board could not be read right now.");
        }

        var club = await clubs.ReadClubByIdAsync(clubId, cancellationToken).ConfigureAwait(false);

        var userIds = board.AllTiles
            .SelectMany(t => t.Helpers.Append(t.ClaimedBy))
            .OfType<string>()
            .Distinct()
            .ToList();
        var members = await clubMembers.ReadClubMembersByUserIdsAsync(userIds, cancellationToken).ConfigureAwait(false);

        return new ClubMissionBoardView(
            clubId,
            club?.Name ?? clubId.ToString(),
            board,
            members.ToDictionary(m => m.Key, m => m.Value.User.Nickname),
            board.ClaimCycleStart(DateTimeOffset.UtcNow, missionBoardConfig.Value.FallbackClaimCycleStart));
    }
}
