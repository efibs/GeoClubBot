using Configuration;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.InactiveMembers;

/// <summary>
/// The club members who have not completed today's (UTC) daily mission yet. A member counts as
/// active once the club activity feed shows a today entry worth exactly the daily-mission XP reward
/// (the same signal the reminder and snapshot jobs use); everyone else on the club roster is
/// reported as inactive. A <c>null</c> <paramref name="ClubId"/> targets the configured main club.
/// </summary>
public sealed record GetTodaysInactiveMembersQuery(Guid? ClubId)
    : IQuery<Result<TodaysInactiveMembers>>;

public sealed record TodaysInactiveMembers(
    Guid ClubId,
    string ClubName,
    DateOnly Day,
    int TotalMembers,
    IReadOnlyList<InactiveMember> Members);

/// <summary>
/// An inactive member, named by their GeoGuessr <paramref name="Nickname"/> plus, when the account
/// is linked, their <paramref name="DiscordUserId"/> so the presentation layer can mention them.
/// </summary>
public sealed record InactiveMember(string Nickname, ulong? DiscordUserId);

public sealed class GetTodaysInactiveMembersHandler(
    IClubMemberRepository clubMembers,
    IClubRepository clubs,
    IGeoGuessrActivityReader activityReader,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    IOptions<DailyMissionReminderConfiguration> reminderConfig)
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

        var dailyMissionXpReward = reminderConfig.Value.DailyMissionXpReward;

        // Cached inside the activity reader (5 min TTL), so repeated admin invocations don't re-hit
        // the GeoGuessr API. This is the only outbound GeoGuessr request in this flow.
        var todaysActivities = await activityReader
            .ReadTodaysActivitiesAsync(clubId, cancellationToken)
            .ConfigureAwait(false);

        var activeUserIds = todaysActivities
            .Where(a => a.XpReward == dailyMissionXpReward)
            .Select(a => a.UserId)
            .ToHashSet();

        var members = await clubMembers
            .ReadClubMembersByClubIdAsync(clubId, cancellationToken)
            .ConfigureAwait(false);

        var inactiveMembers = members
            .Where(m => !activeUserIds.Contains(m.UserId))
            .Select(m => new InactiveMember(m.User.Nickname, m.User.DiscordUserId))
            .OrderBy(m => m.Nickname, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var club = await clubs.ReadClubByIdAsync(clubId, cancellationToken).ConfigureAwait(false);
        var clubName = club?.Name ?? clubId.ToString();

        return new TodaysInactiveMembers(
            clubId,
            clubName,
            DateOnly.FromDateTime(DateTime.UtcNow),
            members.Count,
            inactiveMembers);
    }
}
