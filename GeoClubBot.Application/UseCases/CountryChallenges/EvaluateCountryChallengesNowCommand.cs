using Configuration;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using Utilities;

namespace UseCases.UseCases.CountryChallenges;

/// <summary>
/// Evaluates every country challenge still waiting for its results right now, whether its day has come
/// or not: an admin closing challenges early, or testing a challenge without waiting a day. Only the
/// results — the leaderboard and the day's challenges stay with the regular run.
/// </summary>
public sealed record EvaluateCountryChallengesNowCommand : ICommand<Result<CountryChallengeEvaluationOutcome>>;

public sealed class EvaluateCountryChallengesNowHandler(
    ISender mediator,
    IOptions<CountryChallengesConfiguration> options)
    : IRequestHandler<EvaluateCountryChallengesNowCommand, Result<CountryChallengeEvaluationOutcome>>
{
    public async Task<Result<CountryChallengeEvaluationOutcome>> Handle(
        EvaluateCountryChallengesNowCommand request,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return Error.Validation(RunCountryChallengesHandler.DisabledCode, RunCountryChallengesHandler.DisabledMessage);
        }

        await CountryChallengeRunLock.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var planResult = await mediator.Send(new LoadCountryChallengePlanQuery(), cancellationToken).ConfigureAwait(false);
            if (planResult.IsFailure)
            {
                return planResult.Error;
            }

            var today = CountryChallengeCalendar.Today(DateTimeOffset.UtcNow, options.Value.ResolveTimeZone());

            return await mediator
                .Send(new EvaluateCountryChallengesCommand(planResult.Value, today, IncludeNotYetDue: true), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            CountryChallengeRunLock.Semaphore.Release();
        }
    }
}
