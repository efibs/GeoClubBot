using Constants;
using Microsoft.Extensions.Logging;
using Quartz;
using QuartzExtensions;
using UseCases.InputPorts;

namespace Infrastructure.InputAdapters.Jobs;

[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.DailyChallengesCronScheduleConfigurationKey)]
public class DailyChallengeJob(IDailyChallengeUseCase dailyChallengeUseCase, ILogger<DailyChallengeJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            await dailyChallengeUseCase.CreateDailyChallengeAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create daily challenge.");
        }
    }
}