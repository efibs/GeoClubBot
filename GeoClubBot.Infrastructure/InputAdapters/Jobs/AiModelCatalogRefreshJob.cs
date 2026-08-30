using Configuration;
using Constants;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using QuartzExtensions;
using UseCases.UseCases.AI;

namespace Infrastructure.InputAdapters.Jobs;

/// <summary>
/// Keeps the free-model catalog current. Free models are retired and replaced continuously, so a
/// roster read once at start-up goes stale within a day.
/// </summary>
[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.AiModelCatalogRefreshCronScheduleConfigurationKey)]
public partial class AiModelCatalogRefreshJob(
    ISender mediator,
    IOptions<AiConfiguration> configuration,
    ILogger<AiModelCatalogRefreshJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        // Quartz discovers every IJob in the assembly regardless of feature flags, but the AI services
        // are only registered when the feature is on — so without this guard the job would fail to
        // resolve its collaborators on every tick of an AI-disabled deployment.
        if (!configuration.Value.Active)
        {
            return;
        }

        try
        {
            await mediator.Send(new RefreshChatModelCatalogCommand(), context.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex);
        }
    }

    [LoggerMessage(LogLevel.Error, "Failed to refresh the AI model catalog.")]
    static partial void LogFailed(ILogger<AiModelCatalogRefreshJob> logger, Exception ex);
}
