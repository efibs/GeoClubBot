using Discord;
using Discord.Interactions;
using GeoClubBot.Discord.InputAdapters.Interactions.Activity;
using GeoClubBot.Discord.InputAdapters.Interactions.Autocomplete;
using GeoClubBot.Discord.InputAdapters.Interactions.Help;
using UseCases.UseCases.InactiveMembers;

namespace GeoClubBot.Discord.InputAdapters.Interactions;

// The member-activity group (ActivityModule) is already [DefaultMemberPermissions(Administrator)],
// so this subcommand is admin-only without any extra attribute.
public partial class ActivityModule
{
    [SlashCommand("inactive-members", "Who hasn't kept their streak today, or could still claim a club mission")]
    [HelpDetail("Linked members are shown with their Discord handle, but nobody is pinged.")]
    public Task InactiveMembersAsync(
        [Summary(description: "The club; the main club if left out")]
        [Autocomplete(typeof(ClubAutocompleteHandler))] string? club = null) =>
        ExecuteAsync(
            async ct =>
            {
                if (!ClubOption.TryParse(club, out var clubId))
                {
                    await FollowupAsync(ClubOption.UnknownClubMessage, ephemeral: true).ConfigureAwait(false);
                    return;
                }

                var result = await Mediator
                    .Send(new GetTodaysInactiveMembersQuery(clubId), ct)
                    .ConfigureAwait(false);

                if (result.IsFailure)
                {
                    await FollowupFailureAsync(result.Error).ConfigureAwait(false);
                    return;
                }

                // The report can mention linked members. It is sent ephemerally (only the invoking
                // admin sees it), inside an embed (embed mentions never ping), and with
                // AllowedMentions.None — so the listed members are never notified they were reported.
                await FollowupAsync(
                        embed: InactiveMembersFormatter.BuildEmbed(result.Value).Build(),
                        ephemeral: true,
                        allowedMentions: AllowedMentions.None)
                    .ConfigureAwait(false);
            },
            ephemeral: true,
            failureMessage: "Failed to compute today's inactive members. Please try again later.");
}
