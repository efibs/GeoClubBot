using Configuration;
using Constants;
using Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using QuartzExtensions;
using UseCases.InputPorts.ClubMemberActivity;
using UseCases.InputPorts.Organization;

namespace Infrastructure.InputAdapters.Jobs;

[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.ActivityCheckerCronScheduleConfigurationKey)]
public class ActivityCheckJob(IOptions<GeoGuessrConfiguration> geoGuessrConfig, IServiceProvider serviceProvider, ILogger<ActivityCheckJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var newStatuses = new List<ClubMemberActivityStatus>();

        foreach (var club in geoGuessrConfig.Value.Clubs)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var useCase = scope.ServiceProvider.GetRequiredService<ICheckGeoGuessrPlayerActivityUseCase>();
                var newStatusesOfClub = await useCase.CheckPlayerActivityAsync(club.ClubId).ConfigureAwait(false);
                newStatuses.AddRange(newStatusesOfClub);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error checking player activity for club {ClubId}.", club.ClubId);
            }
        }

        try
        {
            using var scope = serviceProvider.CreateScope();
            var clubMemberActivityRewardUseCase = scope.ServiceProvider.GetRequiredService<IClubMemberActivityRewardUseCase>();

            // Reward player activity
            await clubMemberActivityRewardUseCase
                .RewardMemberActivityAsync(newStatuses).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error rewarding member activity.");
        }

        // Run cleanup after all clubs have been processed, so that all members
        // have history entries before the cleanup deletes members without any
        try
        {
            using var scope = serviceProvider.CreateScope();
            var cleanupUseCase = scope.ServiceProvider.GetRequiredService<ICleanupUseCase>();
            await cleanupUseCase.DoCleanupAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error running cleanup.");
        }
    }
}
