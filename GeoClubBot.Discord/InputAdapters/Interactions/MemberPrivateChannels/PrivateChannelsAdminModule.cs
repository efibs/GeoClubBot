using Discord;
using Discord.Interactions;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using GeoClubBot.Discord.InputAdapters.Interactions.Help;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.UseCases.MemberPrivateChannels;

namespace GeoClubBot.Discord.InputAdapters.Interactions.MemberPrivateChannels;

[CommandContextType(InteractionContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.Administrator)]
[HelpFeature("private-channels", Emoji = "🔒", Title = "Private channels", Order = 110,
    Summary = "Each linked member's own channel, sorted into a category per club.")]
[Group("private-channels-admin", "Manage the members' private channels")]
public class PrivateChannelsAdminModule(
    ISender mediator,
    ILogger<PrivateChannelsAdminModule> logger) : ClubBotInteractionModule(mediator, logger)
{
    [SlashCommand("reconcile",
        "Move every private channel into its club's category, recreate missing ones and fix permissions")]
    public Task ReconcileAsync(
        [Summary("dry-run", "Only report what would change")]
        bool dryRun = true) =>
        ExecuteAsync(
            async ct =>
            {
                var result = await Mediator
                    .Send(new ReconcileMemberPrivateChannelsCommand(dryRun), ct)
                    .ConfigureAwait(false);

                if (result.IsFailure)
                {
                    await FollowupFailureAsync(result.Error).ConfigureAwait(false);
                    return;
                }

                foreach (var message in PrivateChannelsReconcileFormatter.Report(result.Value))
                {
                    await FollowupAsync(message, ephemeral: true, allowedMentions: AllowedMentions.None)
                        .ConfigureAwait(false);
                }
            },
            ephemeral: true,
            failureMessage: "Reconciling the private channels failed. Please check the logs.");
}
