using Discord;
using Discord.Interactions;
using GeoClubBot.Discord.InputAdapters.Interactions.Activity;
using GeoClubBot.Discord.InputAdapters.Interactions.Autocomplete;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using GeoClubBot.Discord.InputAdapters.Interactions.Help;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.UseCases.ClubMemberActivity;
using UseCases.UseCases.GeoGuessrAccountLinking;

namespace GeoClubBot.Discord.InputAdapters.Interactions;

public partial class ActivityModule
{
    public partial class ActivityCurrentWeekModule(
        ISender mediator,
        ILogger<ActivityCurrentWeekModule> logger) : ClubBotInteractionModule(mediator, logger)
    {
        [SlashCommand("by-nickname", "A member's activity since the last weekly check, by GeoGuessr nickname")]
        [HelpDetail("The same view as `/my-activity current-week`, for anyone.")]
        public Task CurrentWeekByNicknameAsync(
            [Autocomplete(typeof(MemberNicknameAutocompleteHandler))]
            [Summary(description: "The member's GeoGuessr nickname, as suggested")] string nickname) =>
            ExecuteAsync(
                async ct =>
                {
                    var geoGuessrUser = await Mediator
                        .Send(new GetGeoGuessrUserByNicknameQuery(nickname), ct)
                        .ConfigureAwait(false);

                    if (geoGuessrUser.IsFailure)
                    {
                        await FollowupAsync($"No club member with the GeoGuessr nickname '**{nickname}**' was found.")
                            .ConfigureAwait(false);
                        return;
                    }

                    var activity = await Mediator
                        .Send(new GetActivityThisWeekQuery(geoGuessrUser.Value.UserId), ct)
                        .ConfigureAwait(false);

                    await FollowupAsync(embed: BuildWeekActivityEmbed(activity, geoGuessrUser.Value.Nickname).Build())
                        .ConfigureAwait(false);
                },
                ephemeral: true,
                failureMessage: "Failed to retrieve the current week activity (internal error). Please try again later. If the issue persists, please contact an admin.");

        [SlashCommand("by-user", "A member's activity since the last weekly check")]
        [UserCommand("Current Week XP")]
        [DefaultMemberPermissions(GuildPermission.Administrator)]
        public Task CurrentWeekByUserAsync([Summary(description: "A member of the server")] IGuildUser user) =>
            ExecuteAsync(
                async ct =>
                {
                    var geoGuessrUser = await Mediator
                        .Send(new GetLinkedGeoGuessrUserQuery(user.Id), ct)
                        .ConfigureAwait(false);

                    if (geoGuessrUser.IsFailure)
                    {
                        await FollowupAsync($"The user '{user.DisplayName}' has not linked their GeoGuessr account yet.")
                            .ConfigureAwait(false);
                        return;
                    }

                    var activity = await Mediator
                        .Send(new GetActivityThisWeekQuery(geoGuessrUser.Value.UserId), ct)
                        .ConfigureAwait(false);

                    await FollowupAsync(embed: BuildWeekActivityEmbed(activity, geoGuessrUser.Value.Nickname).Build())
                        .ConfigureAwait(false);
                },
                ephemeral: true,
                failureMessage: "Failed to retrieve the current week activity (internal error). Please try again later. If the issue persists, please contact an admin.");

        private static EmbedBuilder BuildWeekActivityEmbed(Entities.ClubMemberActivitySummary activity, string nickname) =>
            ActivityProgressFormatter.BuildActivityEmbed(
                activity,
                $"📅 {nickname}'s Activity This Week",
                "✅ All requirements met so far",
                activity.JoinedInPeriod ? $"⭐ {nickname} joined the club on {activity.JoinedDateTime:MMM d}" : null);
    }
}
