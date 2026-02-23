using Configuration;
using Microsoft.Extensions.Options;
using UseCases.InputPorts.Club;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Club;

public class GetClubByNameOrDefaultUseCase(IUnitOfWork unitOfWork, IOptions<GeoGuessrConfiguration> geoGuessrConfig) : IGetClubByNameOrDefaultUseCase
{
    public async Task<Entities.Club?> GetClubByNameOrDefaultAsync(string? clubName)
    {
        // Get the club id
        var (clubId, club) = await _getClubIdAsync(clubName).ConfigureAwait(false);
        
        // If the club could not be found 
        if (clubId is null)
        {
            return null;
        }
        
        // Read the club if not yet given
        club ??= await unitOfWork.Clubs.ReadClubByIdAsync(clubId.Value).ConfigureAwait(false);
        
        return club;
    }
    
    private async Task<(Guid? ClubId, Entities.Club? Club)> _getClubIdAsync(string? clubName)
    {
        // If the club is not set
        if (string.IsNullOrWhiteSpace(clubName))
        {
            // Use the default club id
            return (_defaultClubId, null);
        }
        
        // Look for the club by name
        var club = await unitOfWork.Clubs.ReadClubByNameAsync(clubName).ConfigureAwait(false);

        return (club?.ClubId, club);
    }

    private readonly Guid _defaultClubId = geoGuessrConfig.Value.MainClub.ClubId;
}