using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Rendering;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.ClubMemberActivity;

/// <param name="UserId">The player's GeoGuessr user id.</param>
/// <param name="ClubId">The club whose history is shown; the player's current club when null.</param>
public sealed record RenderPlayerActivityQuery(
    string UserId,
    int MaxNumHistoryEntries,
    Guid? ClubId) : IQuery<Result<PlayerHistoryPlot>>;

/// <summary>The rendered history chart and the nickname of the player it shows. Disposing it disposes the chart.</summary>
public sealed record PlayerHistoryPlot(string Nickname, MemoryStream Plot) : IDisposable
{
    public void Dispose() => Plot.Dispose();
}

/// <summary>Error codes of <see cref="RenderPlayerActivityQuery"/>, each with a message for the member.</summary>
public static class PlayerHistoryErrors
{
    public static readonly Error UnknownPlayer =
        Error.NotFound("player_history.unknown_player", "That player is not known to the bot. Please pick one of the suggested members.");

    public static readonly Error UnknownClub =
        Error.NotFound("player_history.unknown_club", "That club is not tracked by the bot. Please pick one of the suggested clubs.");

    /// <summary>
    /// The history is kept per club and, without a club, read in the player's current one (#162):
    /// one who left has none, but what they did in a club they left can still be shown by naming it.
    /// </summary>
    public static Error NotInAClub(string nickname) =>
        Error.NotFound("player_history.not_in_a_club",
            $"The player '{nickname}' is not in a club at the moment. To see their history in a club they were in, pick that club in the club option.");

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
    : IRequestHandler<RenderPlayerActivityQuery, Result<PlayerHistoryPlot>>
{
    public async Task<Result<PlayerHistoryPlot>> Handle(RenderPlayerActivityQuery request, CancellationToken cancellationToken)
    {
        // A member who left keeps their row (ClubId null) as long as any history refers to it, so a
        // player with history is always found here.
        var member = await clubMembers
            .ReadClubMemberByUserIdAsync(request.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (member is null)
        {
            return PlayerHistoryErrors.UnknownPlayer;
        }

        var nickname = member.User.Nickname;

        Guid clubId;
        if (request.ClubId is not { } requestedClubId)
        {
            if (member.ClubId is not { } currentClubId)
            {
                return PlayerHistoryErrors.NotInAClub(nickname);
            }

            clubId = currentClubId;
        }
        else
        {
            var club = await clubs.ReadClubByIdAsync(requestedClubId, cancellationToken).ConfigureAwait(false);
            if (club is null)
            {
                return PlayerHistoryErrors.UnknownClub;
            }

            clubId = club.ClubId;
        }

        var playersHistoryEntries = await history
            .ReadHistoryEntriesByUserIdAsync(request.UserId, clubId, cancellationToken)
            .ConfigureAwait(false);

        playersHistoryEntries = playersHistoryEntries
            .Prepend(ClubMemberHistoryEntry.Create(member.UserId, member.ClubId ?? clubId, 0, member.JoinedAt))
            .ToList();

        if (playersHistoryEntries.Count < 2)
        {
            return PlayerHistoryErrors.NotEnoughHistory(nickname);
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

        return new PlayerHistoryPlot(nickname, historyRenderer.RenderHistory(values, timestamps, weeklyXpTarget));
    }

    private int ResolveMinXP(ClubMember member)
    {
        var clubEntry = geoGuessrConfig.Value.Clubs.FirstOrDefault(c => c.ClubId == member.ClubId);
        return clubEntry?.GetMinXP(activityCheckerConfig.Value) ?? activityCheckerConfig.Value.MinXP;
    }
}
