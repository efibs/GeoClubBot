using Discord;
using Discord.Interactions;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.UseCases.ClubMemberActivity;

namespace GeoClubBot.Discord.InputAdapters.Interactions;

[CommandContextType(InteractionContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.Administrator)]
[Group("member-activity", "Commands for interacting with the club member activity checker")]
public partial class ActivityModule(
    ISender mediator,
    ILogger<ActivityModule> logger) : ClubBotInteractionModule(mediator, logger)
{
    [Group("strike", "Commands all about the strikes of every player")]
    public partial class ActivityStrikeModule;

    [Group("excuse", "Commands all about excusing players")]
    public partial class ActivityExcuseModule;

    [Group("statistics", "Commands all about statistics about the activity")]
    public partial class ActivityStatisticsModule;

    [Group("current-week", "Commands about a member's current week activity")]
    public partial class ActivityCurrentWeekModule;

    [SlashCommand("last-check-time", "Prints the last time the activities were checked")]
    public async Task LastCheckTimeAsync()
    {
        var lastCheckTime = await Mediator.Send(new GetLastCheckTimeQuery()).ConfigureAwait(false);

        if (lastCheckTime.HasValue)
        {
            await RespondAsync($"The last check was {lastCheckTime:f} UTC.", ephemeral: true).ConfigureAwait(false);
        }
        else
        {
            await RespondAsync("There has not been any checks yet.", ephemeral: true).ConfigureAwait(false);
        }
    }
}
