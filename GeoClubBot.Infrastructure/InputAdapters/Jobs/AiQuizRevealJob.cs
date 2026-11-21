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
/// Shows the answer of every quiz question whose time is up (<c>AI:Quiz:RevealAfterMinutes</c>), in its
/// message (#491). Kept in the database rather than a timer, so a restart reveals what it missed.
/// </summary>
[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.AiQuizRevealCronScheduleConfigurationKey)]
public partial class AiQuizRevealJob(
    ISender mediator,
    IOptions<AiConfiguration> configuration,
    IOptions<AiQuizConfiguration> quiz,
    ILogger<AiQuizRevealJob> logger) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        if (!configuration.Value.Active || !quiz.Value.Enabled)
        {
            return;
        }

        try
        {
            var result = await mediator.Send(new RevealDueQuizRoundsCommand(), cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess && result.Value > 0)
            {
                LogRevealed(logger, result.Value);
            }
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex);
        }
    }

    [LoggerMessage(LogLevel.Information, "Quiz: revealed {Count} question(s) whose time was up.")]
    static partial void LogRevealed(ILogger logger, int count);

    [LoggerMessage(LogLevel.Error, "Failed to reveal due quiz questions.")]
    static partial void LogFailed(ILogger logger, Exception ex);
}
