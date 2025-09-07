using Constants;
using Microsoft.Extensions.Logging;
using Quartz;
using QuartzExtensions;
using UseCases.InputPorts;
using UseCases.InputPorts.Club;

namespace Infrastructure.InputAdapters.Jobs;

[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.ClubLevelCheckerCronScheduleConfigurationKey)]
public class CheckClubLevelJob(ICheckClubLevelUseCase useCase, ILogger<CheckClubLevelJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            await useCase.CheckClubLevelAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking club level.");
        }
    }
}