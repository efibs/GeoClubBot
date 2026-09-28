using Configuration;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using Utilities;

namespace UseCases.UseCases.CountryChallenges;

/// <summary>
/// One run of the country challenges: evaluate the challenges whose results are due, post the
/// leaderboard when it is due, and announce the day's challenges. Every step remembers what it did, so a
/// second run on the same day — the job firing after a manual run, or the other way round — only does
/// what the first one could not.
/// </summary>
/// <param name="Date">The day to run for. Defaults to today in the configured time zone.</param>
public sealed record RunCountryChallengesCommand(DateOnly? Date = null) : ICommand<Result<CountryChallengeRunReport>>;

public sealed partial class RunCountryChallengesHandler(
    ISender mediator,
    IOptions<CountryChallengesConfiguration> options,
    ILogger<RunCountryChallengesHandler> logger)
    : IRequestHandler<RunCountryChallengesCommand, Result<CountryChallengeRunReport>>
{
    public const string DisabledCode = "CountryChallenges.Disabled";

    public const string DisabledMessage = "The country challenges are disabled (CountryChallenges:Enabled is false).";

    public async Task<Result<CountryChallengeRunReport>> Handle(
        RunCountryChallengesCommand request,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return Error.Validation(DisabledCode, DisabledMessage);
        }

        await CountryChallengeRunLock.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var date = request.Date
                       ?? CountryChallengeCalendar.Today(DateTimeOffset.UtcNow, options.Value.ResolveTimeZone());

            var planResult = await mediator.Send(new LoadCountryChallengePlanQuery(), cancellationToken).ConfigureAwait(false);
            if (planResult.IsFailure)
            {
                return planResult.Error;
            }

            var plan = planResult.Value;

            // Isolated from each other like the daily challenge's phases: results that cannot be read
            // must not cost the players today's challenges.
            var evaluation = await RunPhaseAsync(
                "evaluation", () => mediator.Send(new EvaluateCountryChallengesCommand(plan, date), cancellationToken)).ConfigureAwait(false);
            var leaderboard = await RunPhaseAsync(
                "leaderboard", () => mediator.Send(new PostCountryChallengeLeaderboardCommand(plan, date), cancellationToken)).ConfigureAwait(false);
            var announcement = await RunPhaseAsync(
                "announcement", () => mediator.Send(new AnnounceCountryChallengesCommand(plan, date), cancellationToken)).ConfigureAwait(false);

            return new CountryChallengeRunReport(date, plan.Warnings, evaluation, leaderboard, announcement);
        }
        finally
        {
            CountryChallengeRunLock.Semaphore.Release();
        }
    }

    private async Task<T?> RunPhaseAsync<T>(string phase, Func<Task<T>> run) where T : class
    {
        try
        {
            return await run().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogPhaseFailed(logger, ex, phase);
            return null;
        }
    }

    [LoggerMessage(LogLevel.Error, "The country challenge {phase} failed.")]
    static partial void LogPhaseFailed(ILogger<RunCountryChallengesHandler> logger, Exception exception, string phase);
}
