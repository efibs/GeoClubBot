using Configuration;
using Constants;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using QuartzExtensions;
using UseCases.UseCases.AI.Quiz;

namespace Infrastructure.InputAdapters.Jobs;

/// <summary>
/// Has a free vision model rate a batch of guide pictures for the quiz (#491): which questions each can
/// fairly ask, how hard, and the wrong answers. Scheduled after the caption job and before indexing, within
/// its own share of the day's allowance; the index is rated over weeks rather than in one burst.
/// </summary>
[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.AiQuizRatingCronScheduleConfigurationKey)]
public partial class AiQuizRatingJob(
    ISender mediator,
    IOptions<AiConfiguration> configuration,
    IOptions<AiQuizConfiguration> quiz,
    ILogger<AiQuizRatingJob> logger) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        if (!configuration.Value.Active || !quiz.Value.Enabled || !quiz.Value.Rating.Enabled)
        {
            return;
        }

        try
        {
            var result = await mediator.Send(new RateQuizPicturesCommand(), cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                LogRefused(logger, result.Error.Message);
                return;
            }

            var report = result.Value;
            LogCompleted(logger, report.Attempted, report.Rated, report.WithQuestions, report.BudgetExhausted, report.NoModel);
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex);
        }
    }

    [LoggerMessage(LogLevel.Information,
        "Quiz rating run: {Attempted} asked, {Rated} rated, {WithQuestions} make a question (share spent: {BudgetExhausted}, no model: {NoModel}).")]
    static partial void LogCompleted(ILogger logger, int attempted, int rated, int withQuestions, bool budgetExhausted, bool noModel);

    [LoggerMessage(LogLevel.Warning, "Quiz rating run did not start: {Reason}")]
    static partial void LogRefused(ILogger logger, string reason);

    [LoggerMessage(LogLevel.Error, "Failed to run the quiz rating job.")]
    static partial void LogFailed(ILogger logger, Exception ex);
}
