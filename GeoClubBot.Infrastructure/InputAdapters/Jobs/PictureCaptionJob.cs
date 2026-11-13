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
/// Has a free vision model describe a bounded batch of indexed guide pictures that have no words of
/// their own. Scheduled before the nightly indexing run, so its share of the day's allowance is
/// claimed first; it stops early when that share is spent and carries on the next night, so a whole
/// index is described over weeks rather than in one burst.
/// </summary>
[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.AiCaptionCronScheduleConfigurationKey)]
public partial class PictureCaptionJob(
    ISender mediator,
    IOptions<AiConfiguration> configuration,
    IOptions<AiCaptionsConfiguration> captions,
    ILogger<PictureCaptionJob> logger) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        if (!configuration.Value.Active || !captions.Value.Enabled)
        {
            return;
        }

        try
        {
            var result = await mediator.Send(new CaptionPicturesCommand(), cancellationToken).ConfigureAwait(false);

            if (result.IsFailure)
            {
                LogRefused(logger, result.Error.Message);
                return;
            }

            var report = result.Value;
            LogCompleted(logger, report.Attempted, report.Described, report.Unusable, report.Failed, report.Remaining);

            if (report.BudgetExhausted)
            {
                LogBudgetExhausted(logger);
            }

            if (report.NoModel)
            {
                LogNoModel(logger);
            }
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex);
        }
    }

    [LoggerMessage(LogLevel.Information,
        "Picture description run: {Attempted} asked, {Described} described, {Unusable} unusable, {Failed} failed; {Remaining} still waiting.")]
    static partial void LogCompleted(ILogger logger, int attempted, int described, int unusable, int failed, long remaining);

    [LoggerMessage(LogLevel.Information, "Picture description run stopped early: its share of the daily AI allowance is spent.")]
    static partial void LogBudgetExhausted(ILogger logger);

    [LoggerMessage(LogLevel.Warning, "Picture description run found no free model that reads pictures.")]
    static partial void LogNoModel(ILogger logger);

    [LoggerMessage(LogLevel.Warning, "Picture description run did not start: {Reason}")]
    static partial void LogRefused(ILogger logger, string reason);

    [LoggerMessage(LogLevel.Error, "Failed to run the picture description job.")]
    static partial void LogFailed(ILogger logger, Exception ex);
}
