using UseCases.UseCases.CountryChallenges.Configuration;
using Utilities;

namespace UseCases.OutputPorts.CountryChallenges;

/// <summary>
/// Reads the country challenge file. Called on every run, so editing the file takes effect without a
/// restart. A file that cannot be read or parsed is a <see cref="ErrorType.Validation"/> error whose
/// message says where the problem is.
/// </summary>
public interface ICountryChallengeConfigurationSource
{
    Task<Result<CountryChallengesFile>> ReadAsync(CancellationToken cancellationToken = default);
}
