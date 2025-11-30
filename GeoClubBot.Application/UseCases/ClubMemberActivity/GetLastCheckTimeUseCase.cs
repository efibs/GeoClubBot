using Constants;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.ClubMemberActivity;
using UseCases.OutputPorts;

namespace UseCases.UseCases.ClubMemberActivity;

public partial class GetLastCheckTimeUseCase(IUnitOfWork unitOfWork, ILogger<GetLastCheckTimeUseCase> logger, IConfiguration config) : IGetLastCheckTimeUseCase
{
    public async Task<DateTimeOffset?> GetLastCheckTimeAsync()
    {
        // Get the club
        var club = await unitOfWork.Clubs.ReadClubByIdAsync(_clubId).ConfigureAwait(false);

        // If the club was not found
        if (club == null)
        {
            // Log error
            LogClubNotFound(logger, _clubId);
        }
        
        // Get the latest activity check time of the club
        return club?.LatestActivityCheckTime;
    }
    
    private readonly Guid _clubId = config.GetValue<Guid>(ConfigKeys.GeoGuessrClubIdConfigurationKey);
    
    [LoggerMessage(LogLevel.Error, "Club with id {clubId} not found.")]
    static partial void LogClubNotFound(ILogger<GetLastCheckTimeUseCase> logger, Guid clubId);
}