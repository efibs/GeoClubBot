using Discord;
using Discord.Interactions;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using GeoClubBot.Discord.InputAdapters.Interactions.Help;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.UseCases.ClubMemberActivity;

namespace GeoClubBot.Discord.InputAdapters.Interactions;

[CommandContextType(InteractionContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.Administrator)]
[HelpFeature("activity-checks", Emoji = "🛡️", Title = "Activity checks", Order = 40,
    Summary = "Members' activity, the weekly check's strikes and excuses, and activity statistics.")]
[HelpSection(1, "The weekly check", """
    Right after GeoGuessr's weekly board reset I list everyone who missed a requirement (e.g. `missed streak 4/6 · missions 1/2`) and hand out strikes. Members who joined during the week or were excused get lower targets.
    """)]
[Group("member-activity", "Members' activity, strikes, excuses and statistics")]
public partial class ActivityModule(
    ISender mediator,
    ILogger<ActivityModule> logger) : ClubBotInteractionModule(mediator, logger)
{
    [Group("strike", "The strikes the weekly check hands out")]
    public partial class ActivityStrikeModule;

    [Group("excuse", "Excuse members from the weekly check for a while")]
    public partial class ActivityExcuseModule;

    [Group("statistics", "Activity statistics of members and the club")]
    public partial class ActivityStatisticsModule;

    [Group("current-week", "A member's activity since the last weekly check")]
    public partial class ActivityCurrentWeekModule;

    [Group("last-days", "A member's activity over the last days")]
    public partial class ActivityLastDaysModule;

    [SlashCommand("last-check-time", "When the weekly activity check last ran")]
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
