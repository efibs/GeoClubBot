using Discord;
using Discord.Interactions;
using Entities;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using GeoClubBot.Discord.InputAdapters.Interactions.Help;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.UseCases.ClubMemberActivity;
using UseCases.UseCases.GeoGuessrAccountLinking;

namespace GeoClubBot.Discord.InputAdapters.Interactions.Activity;

[CommandContextType(InteractionContextType.Guild)]
[HelpFeature("activity", Emoji = "📊", Title = "Your activity", Order = 30,
    Summary = "Your streak, club missions and XP, and where you stand on the club's weekly requirements.")]
[Group("my-activity", "Your streak, club missions and XP as a club member")]
public class MyActivityModule(
    ISender mediator,
    ILogger<MyActivityModule> logger) : ClubBotInteractionModule(mediator, logger)
{
    [SlashCommand("current-week", "Your streak, club missions and requirements since the last weekly check")]
    [HelpDetail("""
        The week the next check judges (it runs right after GeoGuessr's weekly board reset):
        - XP earned, and your **rule XP**: what averages and swaps count, without the board-clear bonus and with at most 3 missions a week
        - streak days and club missions finished (missions you claimed; helping doesn't count)
        - the requirements and where you stand, e.g. `✅ streak 6/6 · ❌ missions 1/2`
        - the missions of others you pressed **help out** on (for information only)
        - a day-by-day strip: 🟩 streak kept, ⬛ not kept, a digit for missions finished that day
        Needs your account linked.
        """)]
    public Task CurrentWeek() =>
        ExecuteAsync(
            async ct =>
            {
                var geoGuessrUser = await Mediator
                    .Send(new GetLinkedGeoGuessrUserQuery(Context.User.Id), ct)
                    .ConfigureAwait(false);

                if (geoGuessrUser.IsFailure)
                {
                    await SendNotLinkedAsync().ConfigureAwait(false);
                    return;
                }

                var activity = await Mediator
                    .Send(new GetActivityThisWeekQuery(geoGuessrUser.Value.UserId), ct)
                    .ConfigureAwait(false);

                await FollowupAsync(embed: BuildSelfEmbed(activity, "📅 Your Activity This Week", "✅ All requirements met — keep it up!").Build())
                    .ConfigureAwait(false);
            },
            ephemeral: true,
            failureMessage: "Failed to retrieve your current week activity (internal error). Please try again later. If the issue persists, please contact an admin.");

    [SlashCommand("last-days", "Your streak, club missions and XP over the last days")]
    [HelpDetail("A rolling window instead of the check week, so no requirements. GeoGuessr's feed reaches back about two weeks. Needs your account linked.")]
    public Task LastDays(
        [Summary(description: "How many days back to include")]
        [MinValue(1)] [MaxValue(60)] int days = 7) =>
        ExecuteAsync(
            async ct =>
            {
                var geoGuessrUser = await Mediator
                    .Send(new GetLinkedGeoGuessrUserQuery(Context.User.Id), ct)
                    .ConfigureAwait(false);

                if (geoGuessrUser.IsFailure)
                {
                    await SendNotLinkedAsync().ConfigureAwait(false);
                    return;
                }

                var activity = await Mediator
                    .Send(new GetActivityLastDaysQuery(geoGuessrUser.Value.UserId, days), ct)
                    .ConfigureAwait(false);

                await FollowupAsync(embed: BuildSelfEmbed(activity, $"📅 Your Activity — Last {days} Days", $"🔥 Perfect — streak kept on all {days} days!").Build())
                    .ConfigureAwait(false);
            },
            ephemeral: true,
            failureMessage: "Failed to retrieve your activity (internal error). Please try again later. If the issue persists, please contact an admin.");

    private Task SendNotLinkedAsync() =>
        FollowupAsync("You have not yet linked your GeoGuessr account to this Discord account.\n\n" +
                      "Please use the '/gg-account link' command to start linking your GeoGuessr account.");

    private static EmbedBuilder BuildSelfEmbed(ClubMemberActivitySummary activity, string title, string perfectMessage) =>
        ActivityProgressFormatter.BuildActivityEmbed(
            activity,
            title,
            perfectMessage,
            activity.JoinedInPeriod ? $"⭐ You joined the club on {activity.JoinedDateTime:MMM d} — welcome aboard!" : null);
}
