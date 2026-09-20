using Constants;
using MediatR;
using Microsoft.Extensions.Logging;
using Quartz;
using QuartzExtensions;
using UseCases.UseCases.MemberPrivateChannels;

namespace Infrastructure.InputAdapters.Jobs;

[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.MemberPrivateChannelArchiveCleanupCronScheduleConfigurationKey)]
public partial class MemberPrivateChannelArchiveCleanupJob(
    ISender mediator,
    ILogger<MemberPrivateChannelArchiveCleanupJob> logger) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator
                .Send(new DeleteExpiredArchivedPrivateChannelsCommand(), cancellationToken)
                .ConfigureAwait(false);

            if (result.IsSuccess && result.Value > 0)
            {
                LogDeleted(logger, result.Value);
            }
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex);
        }
    }

    [LoggerMessage(LogLevel.Information, "Deleted {ChannelCount} expired archived member private channel(s).")]
    static partial void LogDeleted(ILogger<MemberPrivateChannelArchiveCleanupJob> logger, int channelCount);

    [LoggerMessage(LogLevel.Error, "Failed to delete expired archived member private channels.")]
    static partial void LogFailed(ILogger<MemberPrivateChannelArchiveCleanupJob> logger, Exception ex);
}
