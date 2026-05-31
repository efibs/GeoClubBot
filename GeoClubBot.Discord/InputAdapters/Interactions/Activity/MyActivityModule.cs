using Discord;
using Discord.Interactions;
using Entities;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.UseCases.ClubMemberActivity;
using UseCases.UseCases.GeoGuessrAccountLinking;

namespace GeoClubBot.Discord.InputAdapters.Interactions.Activity;

[CommandContextType(InteractionContextType.Guild)]
[Group("my-activity", "Commands about your activity as a club member")]
public class MyActivityModule(
    ISender mediator,
    ILogger<MyActivityModule> logger) : ClubBotInteractionModule(mediator, logger)
{
    [SlashCommand("current-week", "Prints your daily mission activity this week")]
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

                await FollowupAsync(embed: BuildSelfEmbed(activity, "📅 Your Activity This Week", "🔥 Perfect week so far — keep it up!").Build())
                    .ConfigureAwait(false);
            },
            ephemeral: true,
            failureMessage: "Failed to retrieve your current week activity (internal error). Please try again later. If the issue persists, please contact an admin.");

    [SlashCommand("last-days", "Prints your daily mission activity over the last N days")]
    public Task LastDays(
        [Summary(description: "How many days back to include (1-14, default 7)")]
        [MinValue(1)] [MaxValue(14)] int days = 7) =>
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

                await FollowupAsync(embed: BuildSelfEmbed(activity, $"📅 Your Activity — Last {days} Days", $"🔥 Perfect — all {days} days completed!").Build())
                    .ConfigureAwait(false);
            },
            ephemeral: true,
            failureMessage: "Failed to retrieve your activity (internal error). Please try again later. If the issue persists, please contact an admin.");

    private Task SendNotLinkedAsync() =>
        FollowupAsync("You have not yet linked your GeoGuessr account to this Discord account.\n\n" +
                      "Please use the '/gg-account link' command to start linking your GeoGuessr account.");

    private static EmbedBuilder BuildSelfEmbed(ClubMemberWeekActivity activity, string title, string perfectMessage)
    {
        var embed = ActivityProgressFormatter.BuildActivityEmbed(activity, title);

        if (activity.AllDaysCompleted)
            embed.WithDescription(perfectMessage);

        if (activity.JoinedThisWeek)
            embed.WithFooter($"⭐ You joined the club on {activity.JoinedDateTime:MMM d} — welcome aboard!");

        return embed;
    }
}
