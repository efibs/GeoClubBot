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
/// Indexes a bounded batch of due sources. Runs overnight and stops early when the daily allowance is
/// spent, so a full library is indexed over several nights rather than in one burst that would
/// exhaust the provider's quota and hammer the source sites.
/// </summary>
[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.AiIngestionCronScheduleConfigurationKey)]
public partial class KnowledgeIngestionJob(
    ISender mediator,
    IOptions<AiConfiguration> configuration,
    ILogger<KnowledgeIngestionJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        if (!configuration.Value.Active)
        {
            return;
        }

        try
        {
            var result = await mediator.Send(new IngestKnowledgeSourcesCommand(), context.CancellationToken)
                .ConfigureAwait(false);

            if (result.IsSuccess)
            {
                var report = result.Value;
                LogCompleted(logger, report.Attempted, report.Ingested, report.Unchanged,
                    report.Failed, report.Skipped, report.ChunksWritten);

                if (report.BudgetExhausted)
                {
                    LogBudgetExhausted(logger);
                }
            }
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex);
        }
    }

    [LoggerMessage(LogLevel.Information,
        "Ingestion run: {Attempted} attempted, {Ingested} indexed, {Unchanged} unchanged, {Failed} failed, {Skipped} skipped, {Chunks} chunk(s) written.")]
    static partial void LogCompleted(ILogger logger, int attempted, int ingested, int unchanged, int failed, int skipped, int chunks);

    [LoggerMessage(LogLevel.Warning, "Ingestion stopped early: the daily AI request allowance is spent.")]
    static partial void LogBudgetExhausted(ILogger<KnowledgeIngestionJob> logger);

    [LoggerMessage(LogLevel.Error, "Failed to run AI knowledge ingestion.")]
    static partial void LogFailed(ILogger<KnowledgeIngestionJob> logger, Exception ex);
}
