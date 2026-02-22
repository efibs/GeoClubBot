using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.Club;

namespace GeoClubBot.Discord.InputAdapters.Interactions;

[CommandContextType(InteractionContextType.Guild)]
[Group("club-stats", "Commands for reading a clubs stats")]
public partial class ClubStatsModule(
    IGetClubTodaysXpUseCase getClubTodaysXpUseCase,
    ILogger<ClubStatsModule> logger) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("todays-xp", "Get how much XP a club has achieved today so far")]
    public async Task GetTodaysXpAsync(
        [Summary(description: "[optional] The clubs name")] string? clubName = null,
        [Summary(description: "[optional] include weeklies")] bool includeWeeklies = false)
    {
        try
        {
            // Save input club name
            var inputClubName = clubName;
            
            // Defer the response
            await DeferAsync(ephemeral: false).ConfigureAwait(false);
            
            // Get the XP
            (var xp, clubName) = await getClubTodaysXpUseCase
                .GetTodaysXpAsync(clubName, includeWeeklies)
                .ConfigureAwait(false);
            
            // If the club was not found
            if (clubName is null)
            {
                // Send error
                await FollowupAsync($"The club '{inputClubName ?? "<default>"}' does not exist in the database.", ephemeral: false)
                    .ConfigureAwait(false);
                return;
            }
            
            await FollowupAsync(
                    $"{clubName} currently has {xp} XP today.",
                    ephemeral: false)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogGetTodaysXpFailed(ex, clubName ?? "<default>");
            await FollowupAsync("Failed to fetch the clubs current XP. Please try again later.", ephemeral: false)
                .ConfigureAwait(false);
        }
    }
    
    [LoggerMessage(LogLevel.Error, "Failed to fetch current XP of club {ClubName}.")]
    partial void LogGetTodaysXpFailed(Exception ex, string clubName);
}