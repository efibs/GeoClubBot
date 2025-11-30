using Constants;
using Microsoft.Extensions.Logging;
using Quartz;
using QuartzExtensions;
using UseCases.InputPorts.ClubMemberActivity;

namespace Infrastructure.InputAdapters.Jobs;

[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.ActivityCheckerCronScheduleConfigurationKey)]
public class ActivityCheckJob(ICheckGeoGuessrPlayerActivityUseCase useCase, ILogger<ActivityCheckJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            // Execute the use case
            await useCase.CheckPlayerActivityAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking player activity.");
        }
    }
}