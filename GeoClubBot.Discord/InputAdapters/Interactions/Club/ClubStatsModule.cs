using Discord;
using Discord.Interactions;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.UseCases.Club;

namespace GeoClubBot.Discord.InputAdapters.Interactions.Club;

[CommandContextType(InteractionContextType.Guild)]
[Group("club-stats", "Commands for reading a clubs stats")]
public class ClubStatsModule(
    ISender mediator,
    ILogger<ClubStatsModule> logger) : ClubBotInteractionModule(mediator, logger)
{
    [SlashCommand("todays-xp", "Get how much XP a club has achieved today so far")]
    public Task GetTodaysXpAsync(
        [Summary(description: "[optional] The clubs name")] string? clubName = null,
        [Summary(description: "[optional] include weeklies")] bool includeWeeklies = false) =>
        ExecuteAsync(
            async ct =>
            {
                var inputClubName = clubName;

                var result = await Mediator
                    .Send(new GetClubTodaysXpQuery(clubName, includeWeeklies), ct)
                    .ConfigureAwait(false);

                if (result.ClubName is null)
                {
                    await FollowupAsync($"The club '{inputClubName ?? "<default>"}' does not exist in the database.", ephemeral: false)
                        .ConfigureAwait(false);
                    return;
                }

                await FollowupAsync($"{result.ClubName} currently has {result.Xp} XP today.", ephemeral: false)
                    .ConfigureAwait(false);
            },
            failureMessage: "Failed to fetch the clubs current XP. Please try again later. If the issue persists, please contact an admin.");
}
