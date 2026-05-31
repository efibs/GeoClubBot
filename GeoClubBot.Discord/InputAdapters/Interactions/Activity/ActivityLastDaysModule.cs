using Discord;
using Discord.Interactions;
using GeoClubBot.Discord.InputAdapters.Interactions.Activity;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.UseCases.ClubMemberActivity;
using UseCases.UseCases.GeoGuessrAccountLinking;

namespace GeoClubBot.Discord.InputAdapters.Interactions;

public partial class ActivityModule
{
    public partial class ActivityLastDaysModule(
        ISender mediator,
        ILogger<ActivityLastDaysModule> logger) : ClubBotInteractionModule(mediator, logger)
    {
        [SlashCommand("by-nickname", "Show a member's activity over the last N days by GeoGuessr nickname")]
        public Task LastDaysByNicknameAsync(
            [Summary(description: "The GeoGuessr nickname of the member")] string nickname,
            [Summary(description: "How many days back to include (1-14, default 7)")]
            [MinValue(1)] [MaxValue(14)] int days = 7) =>
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
                        .Send(new GetActivityLastDaysQuery(geoGuessrUser.Value.UserId, days), ct)
                        .ConfigureAwait(false);

                    await FollowupAsync(embed: BuildLastDaysActivityEmbed(activity, geoGuessrUser.Value.Nickname, days).Build())
                        .ConfigureAwait(false);
                },
                ephemeral: true,
                failureMessage: "Failed to retrieve the activity (internal error). Please try again later. If the issue persists, please contact an admin.");

        // Context-menu commands cannot take extra arguments, so the user command uses the default window.
        private const int DefaultDaysBack = 7;

        [SlashCommand("by-user", "Show a Discord user's activity over the last N days")]
        public Task LastDaysByUserAsync(
            IGuildUser user,
            [Summary(description: "How many days back to include (1-14, default 7)")]
            [MinValue(1)] [MaxValue(14)] int days = 7) =>
            ShowLastDaysForUserAsync(user, days);

        [UserCommand("Last 7 Days XP")]
        [DefaultMemberPermissions(GuildPermission.Administrator)]
        public Task LastDaysUserCommandAsync(IGuildUser user) =>
            ShowLastDaysForUserAsync(user, DefaultDaysBack);

        private Task ShowLastDaysForUserAsync(IGuildUser user, int days) =>
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
                        .Send(new GetActivityLastDaysQuery(geoGuessrUser.Value.UserId, days), ct)
                        .ConfigureAwait(false);

                    await FollowupAsync(embed: BuildLastDaysActivityEmbed(activity, geoGuessrUser.Value.Nickname, days).Build())
                        .ConfigureAwait(false);
                },
                ephemeral: true,
                failureMessage: "Failed to retrieve the activity (internal error). Please try again later. If the issue persists, please contact an admin.");

        private static EmbedBuilder BuildLastDaysActivityEmbed(Entities.ClubMemberWeekActivity activity, string nickname, int days)
        {
            var embed = ActivityProgressFormatter.BuildActivityEmbed(activity, $"📅 {nickname}'s Activity — Last {days} Days");

            if (activity.AllDaysCompleted)
                embed.WithDescription($"🔥 Perfect — all {days} days completed!");

            if (activity.JoinedThisWeek)
                embed.WithFooter($"⭐ {nickname} joined the club on {activity.JoinedDateTime:MMM d}");

            return embed;
        }
    }
}
