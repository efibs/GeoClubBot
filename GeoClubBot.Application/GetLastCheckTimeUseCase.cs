using Constants;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts;
using UseCases.OutputPorts;

namespace UseCases;

public class GetLastCheckTimeUseCase(IClubRepository clubRepository, ILogger<GetLastCheckTimeUseCase> logger, IConfiguration config) : IGetLastCheckTimeUseCase
{
    public async Task<DateTimeOffset?> GetLastCheckTimeAsync()
    {
        // Get the club
        var club = await clubRepository.ReadClubByIdAsync(_clubId);

        // If the club was not found
        if (club == null)
        {
            // Log error
            logger.LogError($"Club with id {_clubId} not found.");
        }
        
        // Get the latest activity check time of the club
        return club?.LatestActivityCheckTime;
    }
    
    private readonly Guid _clubId = config.GetValue<Guid>(ConfigKeys.GeoGuessrClubIdConfigurationKey);
}