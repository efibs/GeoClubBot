using Configuration;
using Constants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using QuartzExtensions;
using UseCases.InputPorts.ClubMemberActivity;

namespace Infrastructure.InputAdapters.Jobs;

[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.ActivityCheckerCronScheduleConfigurationKey)]
public class ActivityCheckJob(IOptions<GeoGuessrConfiguration> geoGuessrConfig, IServiceProvider serviceProvider, ILogger<ActivityCheckJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        foreach (var club in geoGuessrConfig.Value.Clubs)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var useCase = scope.ServiceProvider.GetRequiredService<ICheckGeoGuessrPlayerActivityUseCase>();
                await useCase.CheckPlayerActivityAsync(club.ClubId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error checking player activity for club {ClubId}.", club.ClubId);
            }
        }
    }
}
