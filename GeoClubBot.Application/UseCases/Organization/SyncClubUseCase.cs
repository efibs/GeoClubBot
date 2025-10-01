using Constants;
using Microsoft.Extensions.Configuration;
using UseCases.InputPorts.Club;
using UseCases.InputPorts.Organization;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;

namespace UseCases.UseCases.Organization;

public class SyncClubUseCase(
    IGeoGuessrAccess geoGuessrAccess,
    IClubRepository clubRepository,
    ISetClubLevelStatusUseCase setClubLevelStatusUseCase,
    ISaveClubMembersUseCase saveClubMembersUseCase,
    IConfiguration config) : ISyncClubUseCase
{
    public async Task SyncClubAsync()
    {
        // Read the GeoGuessr club
        var geoGuessrClub = await geoGuessrAccess.ReadClubAsync(_clubId).ConfigureAwait(false);

        // Create the club entity
        var club = new Entities.Club
        {
            ClubId = geoGuessrClub.ClubId,
            Name = geoGuessrClub.Name,
            Level = geoGuessrClub.Level
        };
        
        // Sync the club
        await clubRepository.CreateOrUpdateClubAsync(club).ConfigureAwait(false);
        
        // Set the status
        await setClubLevelStatusUseCase.SetClubLevelStatusAsync(club.Level).ConfigureAwait(false);
        
        // Read the members of the club 
        var geoGuessrClubMembers = await geoGuessrAccess.ReadClubMembersAsync(_clubId).ConfigureAwait(false);
        
        // Save the club members
        await saveClubMembersUseCase.SaveClubMembersAsync(geoGuessrClubMembers).ConfigureAwait(false);
    }

    private readonly Guid _clubId = config.GetValue<Guid>(ConfigKeys.GeoGuessrClubIdConfigurationKey);
}