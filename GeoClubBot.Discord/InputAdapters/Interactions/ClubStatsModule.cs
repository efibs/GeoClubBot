using Discord;
using Discord.Interactions;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.Club;

namespace GeoClubBot.Discord.InputAdapters.Interactions;

[CommandContextType(InteractionContextType.Guild)]
[Group("club-stats", "Commands for reading a clubs stats")]
public class ClubStatsModule(
    IGetClubTodaysXpUseCase getClubTodaysXpUseCase,
    ISender mediator,
    ILogger<ClubStatsModule> logger) : ClubBotInteractionModule(mediator, logger)
{
    [SlashCommand("todays-xp", "Get how much XP a club has achieved today so far")]
    public Task GetTodaysXpAsync(
        [Summary(description: "[optional] The clubs name")] string? clubName = null,
        [Summary(description: "[optional] include weeklies")] bool includeWeeklies = false) =>
        ExecuteAsync(
            async _ =>
            {
                var inputClubName = clubName;

                var (xp, resolvedClubName) = await getClubTodaysXpUseCase
                    .GetTodaysXpAsync(clubName, includeWeeklies)
                    .ConfigureAwait(false);

                if (resolvedClubName is null)
                {
                    await FollowupAsync($"The club '{inputClubName ?? "<default>"}' does not exist in the database.", ephemeral: false)
                        .ConfigureAwait(false);
                    return;
                }

                await FollowupAsync($"{resolvedClubName} currently has {xp} XP today.", ephemeral: false)
                    .ConfigureAwait(false);
            },
            failureMessage: "Failed to fetch the clubs current XP. Please try again later. If the issue persists, please contact an admin.");
}
