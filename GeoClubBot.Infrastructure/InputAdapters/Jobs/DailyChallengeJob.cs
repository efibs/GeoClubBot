using Constants;
using Microsoft.Extensions.Logging;
using Quartz;
using QuartzExtensions;
using UseCases.InputPorts;
using UseCases.InputPorts.DailyChallenge;

namespace Infrastructure.InputAdapters.Jobs;

[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.DailyChallengesCronScheduleConfigurationKey)]
public class DailyChallengeJob(IDailyChallengeUseCase dailyChallengeUseCase, ILogger<DailyChallengeJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            await dailyChallengeUseCase.CreateDailyChallengeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create daily challenge.");
        }
    }
}