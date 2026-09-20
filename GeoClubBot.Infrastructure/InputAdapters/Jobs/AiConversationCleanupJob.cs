using Configuration;
using Constants;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using QuartzExtensions;
using UseCases.UseCases.AI.Conversations;
using UseCases.UseCases.AI.Feedback;

namespace Infrastructure.InputAdapters.Jobs;

[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.AiConversationCleanupCronScheduleConfigurationKey)]
public partial class AiConversationCleanupJob(
    ISender mediator,
    IOptions<AiConfiguration> configuration,
    ILogger<AiConversationCleanupJob> logger) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        // Quartz discovers every IJob regardless of feature flags; skip the work when AI is off.
        if (!configuration.Value.Active)
        {
            return;
        }

        try
        {
            var result = await mediator.Send(new PruneAiConversationsCommand(), cancellationToken)
                .ConfigureAwait(false);

            if (result.IsSuccess && result.Value > 0)
            {
                LogPruned(logger, result.Value);
            }
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex);
        }

        try
        {
            // A separate window from the conversation sweep, and off by default: rated conversations
            // outliving unrated ones is the whole point of the archive. Sent separately so a failure
            // to prune one does not skip the other.
            var result = await mediator.Send(new PruneAiFeedbackCommand(), cancellationToken)
                .ConfigureAwait(false);

            if (result.IsSuccess && result.Value > 0)
            {
                LogFeedbackPruned(logger, result.Value);
            }
        }
        catch (Exception ex)
        {
            LogFeedbackPruneFailed(logger, ex);
        }
    }

    [LoggerMessage(LogLevel.Information, "Pruned {TurnCount} expired AI conversation turn(s).")]
    static partial void LogPruned(ILogger<AiConversationCleanupJob> logger, int turnCount);

    [LoggerMessage(LogLevel.Error, "Failed to prune AI conversation history.")]
    static partial void LogFailed(ILogger<AiConversationCleanupJob> logger, Exception ex);

    [LoggerMessage(LogLevel.Information, "Pruned {FeedbackCount} expired AI feedback entr(ies).")]
    static partial void LogFeedbackPruned(ILogger<AiConversationCleanupJob> logger, int feedbackCount);

    [LoggerMessage(LogLevel.Error, "Failed to prune archived AI feedback.")]
    static partial void LogFeedbackPruneFailed(ILogger<AiConversationCleanupJob> logger, Exception ex);
}
