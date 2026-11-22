using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Rendering;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.ClubMemberActivity;

public sealed record RenderPlayerActivityQuery(
    string Nickname,
    int MaxNumHistoryEntries,
    string? ClubName) : IQuery<Result<MemoryStream>>;

/// <summary>Error codes of <see cref="RenderPlayerActivityQuery"/>, each with a message for the member.</summary>
public static class PlayerHistoryErrors
{
    public static Error UnknownPlayer(string nickname) =>
        Error.NotFound("player_history.unknown_player", $"No player with the GeoGuessr nickname '{nickname}' is known to the bot.");

    public static Error UnknownClub(string clubName) =>
        Error.NotFound("player_history.unknown_club", $"No club named '{clubName}' is tracked by the bot.");

    /// <summary>
    /// The history is kept per club and, without a club name, read in the player's current one (#162):
    /// one who left has none, but what they did in a club they left can still be shown by naming it.
    /// </summary>
    public static Error NotInAClub(string nickname) =>
        Error.NotFound("player_history.not_in_a_club",
            $"The player '{nickname}' is not in a club at the moment. To see their history in a club they were in, pick that club in the club name option.");

    public static Error NotEnoughHistory(string nickname) =>
        Error.NotFound("player_history.not_enough_history",
            $"There is no history of the player '{nickname}' in that club yet: it is recorded by the weekly activity check.");
}

public sealed class RenderPlayerActivityHandler(
    IClubRepository clubs,
    IClubMemberRepository clubMembers,
    IHistoryRepository history,
    IHistoryRenderer historyRenderer,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    IOptions<ActivityCheckerConfiguration> activityCheckerConfig)
    : IRequestHandler<RenderPlayerActivityQuery, Result<MemoryStream>>
{
    public async Task<Result<MemoryStream>> Handle(RenderPlayerActivityQuery request, CancellationToken cancellationToken)
    {
        var member = await clubMembers
            .ReadClubMemberByNicknameAsync(request.Nickname, cancellationToken)
            .ConfigureAwait(false);

        Guid clubId;
        if (request.ClubName is null)
        {
            if (member is null)
            {
                return PlayerHistoryErrors.UnknownPlayer(request.Nickname);
            }

            if (member.ClubId is not { } currentClubId)
            {
                return PlayerHistoryErrors.NotInAClub(request.Nickname);
            }

            clubId = currentClubId;
        }
        else
        {
            var club = await clubs.ReadClubByNameAsync(request.ClubName, cancellationToken).ConfigureAwait(false);
            if (club is null)
            {
                return PlayerHistoryErrors.UnknownClub(request.ClubName);
            }

            clubId = club.ClubId;
        }

        var playersHistoryEntries = await history
            .ReadHistoryEntriesByPlayerNicknameAsync(request.Nickname, clubId, cancellationToken)
            .ConfigureAwait(false);

        if (playersHistoryEntries is null)
        {
            return PlayerHistoryErrors.UnknownPlayer(request.Nickname);
        }

        if (member is not null)
        {
            playersHistoryEntries = playersHistoryEntries
                .Prepend(ClubMemberHistoryEntry.Create(member.UserId, member.ClubId ?? clubId, 0, member.JoinedAt))
                .ToList();
        }

        if (playersHistoryEntries.Count < 2)
        {
            return PlayerHistoryErrors.NotEnoughHistory(request.Nickname);
        }

        var weeklyXpTarget = ResolveMinXP(member);

        var entriesOrdered = playersHistoryEntries.OrderBy(e => e.Timestamp);

        var entriesToShow = entriesOrdered
            .Skip(playersHistoryEntries.Count - request.MaxNumHistoryEntries - 1)
            .ToList();

        var values = entriesToShow
            .Skip(1)
            .Zip(entriesToShow, (a, b) => a.Xp - b.Xp)
            .ToList();

        var timestamps = entriesToShow.Select(e => e.Timestamp).ToList();

        return historyRenderer.RenderHistory(values, timestamps, weeklyXpTarget);
    }

    private int ResolveMinXP(ClubMember? member)
    {
        if (member is null)
        {
            return activityCheckerConfig.Value.MinXP;
        }

        var clubEntry = geoGuessrConfig.Value.Clubs.FirstOrDefault(c => c.ClubId == member.ClubId);
        return clubEntry?.GetMinXP(activityCheckerConfig.Value) ?? activityCheckerConfig.Value.MinXP;
    }
}
