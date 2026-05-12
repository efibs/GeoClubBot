using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.ClubMemberActivity;
using UseCases.InputPorts.GeoGuessrAccountLinking;

namespace GeoClubBot.Discord.InputAdapters.Interactions;

public partial class ActivityModule
{
    public partial class ActivityCurrentWeekModule(
        IGetActivityThisWeekUseCase getActivityThisWeekUseCase,
        IGetLinkedGeoGuessrUserUseCase getLinkedGeoGuessrUserUseCase,
        IGetGeoGuessrUserByNicknameUseCase getGeoGuessrUserByNicknameUseCase,
        ILogger<ActivityCurrentWeekModule> logger)
    {
        [SlashCommand("by-nickname", "Show a member's current week activity by GeoGuessr nickname")]
        public async Task CurrentWeekByNicknameAsync(
            [Summary(description: "The GeoGuessr nickname of the member")] string nickname)
        {
            await DeferAsync(ephemeral: true).ConfigureAwait(false);

            try
            {
                var geoGuessrUser = await getGeoGuessrUserByNicknameUseCase
                    .GetGeoGuessrUserByNicknameAsync(nickname)
                    .ConfigureAwait(false);

                if (geoGuessrUser is null)
                {
                    await FollowupAsync($"No club member with the GeoGuessr nickname '**{nickname}**' was found.")
                        .ConfigureAwait(false);
                    return;
                }

                var activity = await getActivityThisWeekUseCase
                    .GetCurrentWeekActivityForMemberAsync(geoGuessrUser.UserId)
                    .ConfigureAwait(false);

                await FollowupAsync(embed: _buildWeekActivityEmbed(activity, geoGuessrUser.Nickname).Build())
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogCurrentWeekByNicknameFailed(logger, ex, nickname);
                await FollowupAsync("Failed to retrieve the current week activity (internal error). Please try again later. If the issue persists, please contact an admin.")
                    .ConfigureAwait(false);
            }
        }

        [SlashCommand("by-user", "Show a Discord user's current week activity")]
        [UserCommand("Current Week XP")]
        [DefaultMemberPermissions(GuildPermission.Administrator)]
        public async Task CurrentWeekByUserAsync(IGuildUser user)
        {
            await DeferAsync(ephemeral: true).ConfigureAwait(false);

            try
            {
                var geoGuessrUser = await getLinkedGeoGuessrUserUseCase
                    .GetLinkedGeoGuessrUserAsync(user.Id)
                    .ConfigureAwait(false);

                if (geoGuessrUser is null)
                {
                    await FollowupAsync($"The user '{user.DisplayName}' has not linked their GeoGuessr account yet.")
                        .ConfigureAwait(false);
                    return;
                }

                var activity = await getActivityThisWeekUseCase
                    .GetCurrentWeekActivityForMemberAsync(geoGuessrUser.UserId)
                    .ConfigureAwait(false);

                await FollowupAsync(embed: _buildWeekActivityEmbed(activity, geoGuessrUser.Nickname).Build())
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogCurrentWeekByUserFailed(logger, ex, user.DisplayName);
                await FollowupAsync("Failed to retrieve the current week activity (internal error). Please try again later. If the issue persists, please contact an admin.")
                    .ConfigureAwait(false);
            }
        }

        private static EmbedBuilder _buildWeekActivityEmbed(Entities.ClubMemberWeekActivity activity, string nickname)
        {
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
                .WithTitle($"📅 {nickname}'s Activity This Week")
                .WithColor(new Color(0x1A, 0xBC, 0x9C))
                .AddField("🏆 XP Earned", $"**{activity.TotalXp:N0} XP**", inline: true)
                .AddField("📆 Days Completed", $"**{activity.NumDaysDone} / {activity.DailyMissions.Count}**", inline: true)
                .AddField("Progress", progressValue);

            if (activity.AllDaysCompleted)
                embed.WithDescription("🔥 Perfect week so far");

            if (activity.JoinedThisWeek)
                embed.WithFooter($"⭐ {nickname} joined the club on {activity.JoinedDateTime:MMM d}");

            return embed;
        }

        [LoggerMessage(LogLevel.Error, "Failed to get current week activity for GeoGuessr nickname '{nickname}'.")]
        static partial void LogCurrentWeekByNicknameFailed(ILogger<ActivityCurrentWeekModule> logger, Exception ex, string nickname);

        [LoggerMessage(LogLevel.Error, "Failed to get current week activity for Discord user '{displayName}'.")]
        static partial void LogCurrentWeekByUserFailed(ILogger<ActivityCurrentWeekModule> logger, Exception ex, string displayName);
    }
}
