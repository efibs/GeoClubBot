using Configuration;
using Constants;
using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using QuartzExtensions;
using UseCases.UseCases.ClubMemberActivity;
using UseCases.UseCases.Organization;

namespace Infrastructure.InputAdapters.Jobs;

[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.ActivityCheckerCronScheduleConfigurationKey)]
public class ActivityCheckJob(
    ISender mediator,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    ILogger<ActivityCheckJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var newStatuses = new List<ClubMemberActivityStatus>();

        foreach (var club in geoGuessrConfig.Value.Clubs)
        {
            try
            {
                var newStatusesOfClub = await mediator
                    .Send(new CheckGeoGuessrPlayerActivityCommand(club.ClubId), context.CancellationToken)
                    .ConfigureAwait(false);
                newStatuses.AddRange(newStatusesOfClub);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error checking player activity for club {ClubId}.", club.ClubId);
            }
        }

        try
        {
            await mediator
                .Send(new ClubMemberActivityRewardCommand(newStatuses), context.CancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error rewarding member activity.");
        }

        // Cleanup runs last so deletions don't strand members without history entries.
        try
        {
            await mediator.Send(new CleanupCommand(), context.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error running cleanup.");
        }
    }
}
