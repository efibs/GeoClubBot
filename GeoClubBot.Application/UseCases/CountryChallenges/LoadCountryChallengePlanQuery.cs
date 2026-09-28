using MediatR;
using UseCases.Abstractions;
using UseCases.OutputPorts.CountryChallenges;
using UseCases.UseCases.CountryChallenges.Configuration;
using Utilities;

namespace UseCases.UseCases.CountryChallenges;

/// <summary>
/// Reads and resolves the country challenge file. Fails with a <see cref="ErrorType.Validation"/> error
/// that lists every problem found, one per line.
/// </summary>
public sealed record LoadCountryChallengePlanQuery : IQuery<Result<CountryChallengePlan>>;

public sealed class LoadCountryChallengePlanHandler(ICountryChallengeConfigurationSource source)
    : IRequestHandler<LoadCountryChallengePlanQuery, Result<CountryChallengePlan>>
{
    public const string InvalidConfigurationCode = "CountryChallenges.InvalidConfiguration";

    public async Task<Result<CountryChallengePlan>> Handle(LoadCountryChallengePlanQuery request, CancellationToken cancellationToken)
    {
        var file = await source.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (file.IsFailure)
        {
            return file.Error;
        }

        var resolution = CountryChallengePlanResolver.Resolve(file.Value);
        if (resolution.Plan is null)
        {
            return Error.Validation(
                InvalidConfigurationCode,
                string.Join("\n", resolution.Errors.Select(e => $"• {e}")));
        }

        return resolution.Plan;
    }
}
