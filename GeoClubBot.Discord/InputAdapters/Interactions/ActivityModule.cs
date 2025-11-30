using Discord;
using Discord.Interactions;
using UseCases.InputPorts.ClubMemberActivity;

namespace GeoClubBot.Discord.InputAdapters.Interactions;

[CommandContextType(InteractionContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.Administrator)]
[Group("member-activity", "Commands for interacting with the club member activity checker")]
public partial class ActivityModule(IGetLastCheckTimeUseCase getLastCheckTimeUseCase) : InteractionModuleBase<SocketInteractionContext>
{
    [Group("strike", "Commands all about the strikes of every player")]
    public partial class ActivityStrikeModule : InteractionModuleBase<SocketInteractionContext>;
    
    [Group("excuse", "Commands all about excusing players")]
    public partial class ActivityExcuseModule : InteractionModuleBase<SocketInteractionContext>;

    [Group("statistics", "Commands all about statistics about the activity")]
    public partial class ActivityStatisticsModule : InteractionModuleBase<SocketInteractionContext>;
    
    [SlashCommand("last-check-time", "Prints the last time the activities were checked")]
    public async Task LastCheckTimeAsync()
    {
        // Get the last check time
        var lastCheckTime = await getLastCheckTimeUseCase.GetLastCheckTimeAsync().ConfigureAwait(false);
        
        // If there is a last check time
        if (lastCheckTime.HasValue)
        {
            // Respond
            await RespondAsync($"The last check was {lastCheckTime:f} UTC.", ephemeral: true).ConfigureAwait(false);
        }
        else
        {
            // Respond
            await RespondAsync("There has not been any checks yet.", ephemeral: true).ConfigureAwait(false);
        }
    }
}