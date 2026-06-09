using Discord;
using Discord.Interactions;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.UseCases.DailyMissionStatistics;
// The last namespace segment shadows the equally named DTO record, so alias it back in.
using MissionStatistics = UseCases.UseCases.DailyMissionStatistics.DailyMissionStatistics;

namespace GeoClubBot.Discord.InputAdapters.Interactions.DailyMissionStatistics;

[CommandContextType(InteractionContextType.Guild)]
[Group("daily-missions", "Statistics about the GeoGuessr daily missions")]
public class DailyMissionStatisticsModule(
    ISender mediator,
    ILogger<DailyMissionStatisticsModule> logger) : ClubBotInteractionModule(mediator, logger)
{
    [SlashCommand("stats", "Show statistics about the GeoGuessr daily missions")]
    public Task StatsAsync(
        [Summary(description: "How many days back to include (1-365, default 30)")]
        [MinValue(1)] [MaxValue(365)] int days = 30,
        [Summary(description: "Show details for a single mission kind")]
        [Autocomplete(typeof(MissionKindAutocompleteHandler))] string? mission = null,
        [Summary(description: "Restrict the statistics to one club (default: all clubs)")]
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
                    .Send(new GetDailyMissionStatisticsQuery(clubId, days), ct)
                    .ConfigureAwait(false);

                if (result.IsFailure)
                {
                    await FollowupFailureAsync(result.Error).ConfigureAwait(false);
                    return;
                }

                var stats = result.Value;

                if (mission == null)
                {
                    await FollowupAsync(embed: DailyMissionStatisticsFormatter.BuildOverviewEmbed(stats).Build())
                        .ConfigureAwait(false);
                    return;
                }

                var kind = FindKind(stats, mission);
                if (kind == null)
                {
                    await FollowupAsync(
                            $"That mission did not appear between {stats.FromDay:yyyy-MM-dd} and {stats.ToDay:yyyy-MM-dd}. "
                            + "Try a larger time range or pick one of the suggested missions.",
                            ephemeral: true)
                        .ConfigureAwait(false);
                    return;
                }

                await FollowupAsync(embed: DailyMissionStatisticsFormatter.BuildDetailEmbed(stats, kind).Build())
                    .ConfigureAwait(false);
            },
            ephemeral: true,
            failureMessage: "Failed to compute the daily mission statistics. Please try again later.");

    /// <summary>
    /// Resolves the mission option against the computed kinds. The autocomplete sends
    /// "Type|GameMode", but users can also submit free text, so the friendly label is
    /// accepted as a fallback.
    /// </summary>
    private static DailyMissionKindStatistics? FindKind(MissionStatistics stats, string mission)
    {
        var parts = mission.Split('|', 2);
        if (parts.Length == 2)
        {
            var byValue = stats.Kinds.FirstOrDefault(k =>
                k.Type.Equals(parts[0], StringComparison.OrdinalIgnoreCase)
                && k.GameMode.Equals(parts[1], StringComparison.OrdinalIgnoreCase));

            if (byValue != null)
            {
                return byValue;
            }
        }

        return stats.Kinds.FirstOrDefault(k =>
            DailyMissionStatisticsFormatter.KindLabel(k.Type, k.GameMode)
                .Equals(mission, StringComparison.OrdinalIgnoreCase));
    }
}
