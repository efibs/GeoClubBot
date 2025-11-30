using Constants;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.ClubMemberActivity;
using UseCases.OutputPorts;

namespace UseCases.UseCases.ClubMemberActivity;

public class GetLastCheckTimeUseCase(IUnitOfWork unitOfWork, ILogger<GetLastCheckTimeUseCase> logger, IConfiguration config) : IGetLastCheckTimeUseCase
{
    public async Task<DateTimeOffset?> GetLastCheckTimeAsync()
    {
        // Get the club
        var club = await unitOfWork.Clubs.ReadClubByIdAsync(_clubId).ConfigureAwait(false);

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