using Discord;
using Discord.Interactions;
using GeoClubBot.Discord.InputAdapters.Interactions.Autocomplete;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.UseCases.Club;
using UseCases.UseCases.MissionBoard;

namespace GeoClubBot.Discord.InputAdapters.Interactions.Club;

[CommandContextType(InteractionContextType.Guild)]
[Group("club-stats", "Commands for reading a clubs stats")]
public class ClubStatsModule(
    ISender mediator,
    ILogger<ClubStatsModule> logger) : ClubBotInteractionModule(mediator, logger)
{
    [SlashCommand("todays-xp", "Get how much XP a club has achieved today so far")]
    public Task GetTodaysXpAsync(
        [Autocomplete(typeof(ClubNameAutocompleteHandler))][Summary(description: "[optional] The clubs name")] string? clubName = null) =>
        ExecuteAsync(
            async ct =>
            {
                var inputClubName = clubName;

                var result = await Mediator
                    .Send(new GetClubTodaysXpQuery(clubName), ct)
                    .ConfigureAwait(false);

                if (result.ClubName is null)
                {
                    await FollowupAsync($"The club '{inputClubName ?? "<default>"}' does not exist in the database.", ephemeral: false)
                        .ConfigureAwait(false);
                    return;
                }

                // Separate counts: the streak and the mission board are independent XP sources, so
                // one number would hide half the picture.
                var claims = result.ClaimMemberCount is { } claimCount
                    ? $" · Claimed a mission this claim cycle: {claimCount}/{result.TotalMemberCount}"
                    : string.Empty;
                await FollowupAsync(
                        $"{result.ClubName} currently has {result.Xp} XP today (UTC). "
                        + $"Streak: {result.ChallengeMemberCount}/{result.TotalMemberCount} · "
                        + $"Missions finished: {result.BoardMissionCount}"
                        + claims,
                        ephemeral: false)
                    .ConfigureAwait(false);
            },
            failureMessage: "Failed to fetch the clubs current XP. Please try again later. If the issue persists, please contact an admin.");

    [SlashCommand("board", "Show this week's club mission boards and who holds which open mission")]
    public Task GetMissionBoardAsync(
        [Summary(description: "[optional] The club (default: the main club)")]
        [Autocomplete(typeof(ClubAutocompleteHandler))] string? club = null) =>
        ExecuteAsync(
            async ct =>
            {
                Guid? clubId = null;
                if (club != null)
                {
                    if (!Guid.TryParse(club, out var parsedClubId))
                    {
                        await FollowupAsync("Unknown club. Please pick one of the suggested clubs.", ephemeral: true)
                            .ConfigureAwait(false);
                        return;
                    }

                    clubId = parsedClubId;
                }

                var result = await Mediator
                    .Send(new GetClubMissionBoardQuery(clubId), ct)
                    .ConfigureAwait(false);

                if (result.IsFailure)
                {
                    await FollowupFailureAsync(result.Error).ConfigureAwait(false);
                    return;
                }

                // Nicknames only, in an embed: nobody is pinged by someone checking the board.
                await FollowupAsync(
                        embed: ClubMissionBoardFormatter.BuildEmbed(result.Value).Build(),
                        allowedMentions: AllowedMentions.None)
                    .ConfigureAwait(false);
            },
            failureMessage: "Failed to read the club's mission board. Please try again later. If the issue persists, please contact an admin.");
}
