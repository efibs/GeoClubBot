using Microsoft.Extensions.Options;

namespace Configuration;

/// <summary>
/// Fails start-up when the website is enabled without a usable second club, instead of at the
/// first request. The club is read through its own GeoGuessr:Clubs entry (token and resilience
/// pipeline), so it has to be one of them.
/// </summary>
public sealed class WebsiteConfigurationValidator(IOptions<GeoGuessrConfiguration> geoGuessrConfig)
    : IValidateOptions<WebsiteConfiguration>
{
    private const string SecondClubIdKey = $"{WebsiteConfiguration.SectionName}:{nameof(WebsiteConfiguration.SecondClubId)}";

    public ValidateOptionsResult Validate(string? name, WebsiteConfiguration options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        if (options.SecondClubId is not { } secondClubId)
        {
            return ValidateOptionsResult.Fail($"{SecondClubIdKey} is required when the website is enabled.");
        }

        var club = geoGuessrConfig.Value.Clubs.FirstOrDefault(c => c.ClubId == secondClubId);
        if (club is null)
        {
            return ValidateOptionsResult.Fail($"{SecondClubIdKey} {secondClubId} is not one of the GeoGuessr:Clubs.");
        }

        return club.IsMain
            ? ValidateOptionsResult.Fail($"{SecondClubIdKey} {secondClubId} is the main club.")
            : ValidateOptionsResult.Success;
    }
}
