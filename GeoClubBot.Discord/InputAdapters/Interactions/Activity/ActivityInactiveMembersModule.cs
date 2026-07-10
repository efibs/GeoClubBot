using Discord;
using Discord.Interactions;
using GeoClubBot.Discord.InputAdapters.Interactions.Activity;
using GeoClubBot.Discord.InputAdapters.Interactions.DailyMissionStatistics;
using UseCases.UseCases.InactiveMembers;

namespace GeoClubBot.Discord.InputAdapters.Interactions;

// The member-activity group (ActivityModule) is already [DefaultMemberPermissions(Administrator)],
// so this subcommand is admin-only without any extra attribute.
public partial class ActivityModule
{
    [SlashCommand("inactive-members", "List today's club members who haven't done their daily mission")]
    public Task InactiveMembersAsync(
        [Summary(description: "Restrict to one club (default: the main club)")]
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
