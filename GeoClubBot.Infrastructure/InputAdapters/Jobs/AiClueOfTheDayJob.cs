using Configuration;
using Constants;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using QuartzExtensions;
using UseCases.UseCases.AI.ClueOfTheDay;

namespace Infrastructure.InputAdapters.Jobs;

/// <summary>
/// Posts one lesser-known clue with its picture to the configured channel. Runs on
/// <c>AI:ClueOfTheDaySchedule</c> in <c>AI:ClueOfTheDayTimeZone</c> (UTC unless set), and does nothing
/// while the feature is off.
/// </summary>
[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.AiClueOfTheDayCronScheduleConfigurationKey, ConfigKeys.AiClueOfTheDayTimeZoneConfigurationKey)]
public partial class AiClueOfTheDayJob(
    ISender mediator,
    IOptions<AiConfiguration> aiConfiguration,
    IOptions<AiClueOfTheDayConfiguration> configuration,
    ILogger<AiClueOfTheDayJob> logger) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        if (!aiConfiguration.Value.Active || !configuration.Value.Enabled)
        {
            return;
        }

        try
        {
            var result = await mediator.Send(new PostClueOfTheDayCommand(), cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                LogNotPosted(logger, result.Error.Code, result.Error.Message);
            }
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex);
        }
    }

    [LoggerMessage(LogLevel.Warning, "The clue of the day was not posted ({Code}): {Reason}")]
    static partial void LogNotPosted(ILogger logger, string code, string reason);

    [LoggerMessage(LogLevel.Error, "Failed to run the clue of the day.")]
    static partial void LogFailed(ILogger logger, Exception ex);
}
