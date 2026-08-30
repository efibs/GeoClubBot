using Configuration;
using Constants;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using QuartzExtensions;
using UseCases.UseCases.AI.Ingestion;

namespace Infrastructure.InputAdapters.Jobs;

/// <summary>
/// Refreshes the catalogue of known guide sources. Cheap compared with ingestion, so it runs on its
/// own schedule and keeps the queue populated between the slower indexing runs.
/// </summary>
[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.AiCatalogSyncCronScheduleConfigurationKey)]
public partial class KnowledgeCatalogSyncJob(
    ISender mediator,
    IOptions<AiConfiguration> configuration,
    ILogger<KnowledgeCatalogSyncJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        // Quartz discovers every IJob regardless of feature flags; skip the work when AI is off.
        if (!configuration.Value.Active)
        {
            return;
        }

        try
        {
            await mediator.Send(new SyncSourceCatalogsCommand(), context.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex);
        }
    }

    [LoggerMessage(LogLevel.Error, "Failed to sync the AI source catalog.")]
    static partial void LogFailed(ILogger<KnowledgeCatalogSyncJob> logger, Exception ex);
}
