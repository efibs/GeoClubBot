using Configuration;
using Constants;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using QuartzExtensions;
using UseCases.UseCases.AI.Conversations;

namespace Infrastructure.InputAdapters.Jobs;

[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.AiConversationCleanupCronScheduleConfigurationKey)]
public partial class AiConversationCleanupJob(
    ISender mediator,
    IOptions<AiConfiguration> configuration,
    ILogger<AiConversationCleanupJob> logger) : IJob
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
            var result = await mediator.Send(new PruneAiConversationsCommand(), context.CancellationToken)
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
    }

    [LoggerMessage(LogLevel.Information, "Pruned {TurnCount} expired AI conversation turn(s).")]
    static partial void LogPruned(ILogger<AiConversationCleanupJob> logger, int turnCount);

    [LoggerMessage(LogLevel.Error, "Failed to prune AI conversation history.")]
    static partial void LogFailed(ILogger<AiConversationCleanupJob> logger, Exception ex);
}
