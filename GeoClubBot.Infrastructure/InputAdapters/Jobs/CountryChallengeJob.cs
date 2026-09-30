using Configuration;
using Constants;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using QuartzExtensions;
using UseCases.UseCases.CountryChallenges;

namespace Infrastructure.InputAdapters.Jobs;

[DisallowConcurrentExecution]
[ConfiguredCronJob(
    ConfigKeys.CountryChallengesCronScheduleConfigurationKey,
    ConfigKeys.CountryChallengesTimeZoneConfigurationKey)]
public partial class CountryChallengeJob(
    ISender mediator,
    IOptions<CountryChallengesConfiguration> options,
    ILogger<CountryChallengeJob> logger) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        // Quartz discovers every IJob regardless of feature flags; skip the work when the feature is off.
        if (!options.Value.Enabled)
        {
            return;
        }

        try
        {
            var result = await mediator.Send(new RunCountryChallengesCommand(), cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                // Most often a broken file. Logged as an error so it reaches the Discord log channel.
                LogRunRejected(logger, result.Error.Message);
                return;
            }

            Report(result.Value);
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex);
        }
    }

    private void Report(CountryChallengeRunReport report)
    {
        foreach (var warning in report.Warnings)
        {
            LogConfigurationWarning(logger, warning);
        }

        var evaluated = report.Evaluation?.Evaluated ?? [];
        var announced = report.Announcement?.Announced ?? [];

        LogRun(logger, report.Date, evaluated.Count, report.Leaderboard?.Status.ToString() ?? "failed", string.Join(", ", announced));

        if (report.Evaluation?.Failed is { Count: > 0 } unevaluated)
        {
            LogEvaluationPending(logger, string.Join(", ", unevaluated));
        }

        if (report.Announcement?.Failed is { Count: > 0 } unannounced)
        {
            LogAnnouncementFailed(logger, report.Date, string.Join(", ", unannounced));
        }
    }

    [LoggerMessage(LogLevel.Information, "Country challenges of {date}: evaluated {evaluatedCount}, leaderboard {leaderboard}, announced [{announced}].")]
    static partial void LogRun(ILogger<CountryChallengeJob> logger, DateOnly date, int evaluatedCount, string leaderboard, string announced);

    [LoggerMessage(LogLevel.Warning, "The results of these country challenges could not be evaluated and are retried at the next run: {challenges}.")]
    static partial void LogEvaluationPending(ILogger<CountryChallengeJob> logger, string challenges);

    [LoggerMessage(LogLevel.Warning, "These country challenges of {date} were not announced: {challenges}. Use /country-challenges-admin post-now to try again today.")]
    static partial void LogAnnouncementFailed(ILogger<CountryChallengeJob> logger, DateOnly date, string challenges);

    [LoggerMessage(LogLevel.Warning, "Country challenge configuration: {warning}")]
    static partial void LogConfigurationWarning(ILogger<CountryChallengeJob> logger, string warning);

    [LoggerMessage(LogLevel.Error, "The country challenges did not run:\n{reason}")]
    static partial void LogRunRejected(ILogger<CountryChallengeJob> logger, string reason);

    [LoggerMessage(LogLevel.Error, "The country challenge run failed.")]
    static partial void LogFailed(ILogger<CountryChallengeJob> logger, Exception ex);
}
