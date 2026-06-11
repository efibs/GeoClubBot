using Discord;
using Discord.Interactions;
using GeoClubBot.Discord.InputAdapters.Interactions.Activity;
using GeoClubBot.Discord.InputAdapters.Interactions.Autocomplete;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
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
        [SlashCommand("by-nickname", "Show a member's current week activity by GeoGuessr nickname")]
        public Task CurrentWeekByNicknameAsync(
            [Autocomplete(typeof(MemberNicknameAutocompleteHandler))]
            [Summary(description: "The GeoGuessr nickname of the member")] string nickname) =>
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

        [SlashCommand("by-user", "Show a Discord user's current week activity")]
        [UserCommand("Current Week XP")]
        [DefaultMemberPermissions(GuildPermission.Administrator)]
        public Task CurrentWeekByUserAsync(IGuildUser user) =>
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

        private static EmbedBuilder BuildWeekActivityEmbed(Entities.ClubMemberWeekActivity activity, string nickname)
        {
            var embed = ActivityProgressFormatter.BuildActivityEmbed(activity, $"📅 {nickname}'s Activity This Week");

            if (activity.AllDaysCompleted)
                embed.WithDescription("🔥 Perfect week so far");

            if (activity.JoinedThisWeek)
                embed.WithFooter($"⭐ {nickname} joined the club on {activity.JoinedDateTime:MMM d}");

            return embed;
        }
    }
}
