using Configuration;
using Constants;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using QuartzExtensions;
using UseCases.UseCases.AI.KnowledgeGaps;

namespace Infrastructure.InputAdapters.Jobs;

/// <summary>Forgets old weak retrievals and, when configured, posts the knowledge-gap report.</summary>
[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.AiKnowledgeGapsCronScheduleConfigurationKey)]
public partial class AiKnowledgeGapReportJob(
    ISender mediator,
    IOptions<AiConfiguration> configuration,
    ILogger<AiKnowledgeGapReportJob> logger) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        if (!configuration.Value.Active)
        {
            return;
        }

        try
        {
            var result = await mediator.Send(new PostKnowledgeGapReportCommand(), cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                LogNotPosted(logger, result.Error.Message);
            }
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex);
        }
    }

    [LoggerMessage(LogLevel.Warning, "The knowledge-gap report was not posted: {Reason}")]
    static partial void LogNotPosted(ILogger logger, string reason);

    [LoggerMessage(LogLevel.Error, "Failed to run the knowledge-gap report.")]
    static partial void LogFailed(ILogger logger, Exception ex);
}
