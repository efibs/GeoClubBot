using Discord;
using Discord.Interactions;
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
                    await FollowupAsync("You have not yet linked your GeoGuessr account to this Discord account.\n\n" +
                                        "Please use the '/gg-account link' command to start linking your GeoGuessr account.")
                        .ConfigureAwait(false);
                    return;
                }

                var activity = await Mediator
                    .Send(new GetActivityThisWeekQuery(geoGuessrUser.Value.UserId), ct)
                    .ConfigureAwait(false);

                // Build two-row progress display: day labels + emoji per day
                var labelRow = string.Join(" ", activity.DailyMissions.Select(d => d.Date.DayOfWeek switch
                {
                    DayOfWeek.Monday    => "Mo",
                    DayOfWeek.Tuesday   => "Tu",
                    DayOfWeek.Wednesday => "We",
                    DayOfWeek.Thursday  => "Th",
                    DayOfWeek.Friday    => "Fr",
                    DayOfWeek.Saturday  => "Sa",
                    _                   => "Su"
                }));
                var emojiRow = string.Join(" ", activity.DailyMissions.Select(d => d.MissionCompleted ? "🟩" : "⬛"));
                var progressValue = activity.DailyMissions.Count > 0
                    ? $"`{labelRow}`\n{emojiRow}"
                    : "No days tracked yet";

                var embed = new EmbedBuilder()
                    .WithTitle("📅 Your Activity This Week")
                    .WithColor(new Color(0x1A, 0xBC, 0x9C))
                    .AddField("🏆 XP Earned", $"**{activity.TotalXp:N0} XP**", inline: true)
                    .AddField("📆 Days Completed", $"**{activity.NumDaysDone} / {activity.DailyMissions.Count}**", inline: true)
                    .AddField("Progress", progressValue);

                if (activity.AllDaysCompleted)
                    embed.WithDescription("🔥 Perfect week so far — keep it up!");

                if (activity.JoinedThisWeek)
                    embed.WithFooter($"⭐ You joined the club on {activity.JoinedDateTime:MMM d} — welcome aboard!");

                await FollowupAsync(embed: embed.Build()).ConfigureAwait(false);
            },
            ephemeral: true,
            failureMessage: "Failed to retrieve your current week activity (internal error). Please try again later. If the issue persists, please contact an admin.");
}
