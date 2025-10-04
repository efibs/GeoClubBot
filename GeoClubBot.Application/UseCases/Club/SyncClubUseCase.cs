using Constants;
using Entities;
using Microsoft.Extensions.Configuration;
using UseCases.InputPorts.Club;
using UseCases.InputPorts.ClubMembers;
using UseCases.InputPorts.Organization;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;

namespace UseCases.UseCases.Club;

public class SyncClubUseCase(
    IGeoGuessrAccess geoGuessrAccess,
    IClubRepository clubRepository,
    IClubMemberRepository clubMemberRepository,
    ISetClubLevelStatusUseCase setClubLevelStatusUseCase,
    ISaveClubMembersUseCase saveClubMembersUseCase,
    IConfiguration config) : ISyncClubUseCase
{
    public async Task SyncClubAsync()
    {
        // Read the GeoGuessr club
        var club = await geoGuessrAccess.ReadClubAsync(_clubId).ConfigureAwait(false);
        
        // Sync the club
        await clubRepository.CreateOrUpdateClubAsync(club).ConfigureAwait(false);
        
        // Set the status
        await setClubLevelStatusUseCase.SetClubLevelStatusAsync(club.Level).ConfigureAwait(false);
        
        // Read the current members of the club 
        var geoGuessrClubMembers = await geoGuessrAccess.ReadClubMembersAsync(_clubId).ConfigureAwait(false);
        
        // Read the members from the database
        var databaseClubMembers = await clubMemberRepository.ReadClubMembersAsync().ConfigureAwait(false);
        
        // Join the two lists
        var toSaveClubMembers = _joinClubMembersList(geoGuessrClubMembers, databaseClubMembers);
        
        // Save the club members
        await saveClubMembersUseCase.SaveClubMembersAsync(toSaveClubMembers).ConfigureAwait(false);
    }

    private List<ClubMember> _joinClubMembersList(List<ClubMember> geoGuessrCurrentClubMembers,
        IEnumerable<ClubMember> databaseClubMembers)
    {
        // The resulting joined list
        var joinedClubMembers = new List<ClubMember>();
        
        // Append the geoGuessr club members
        joinedClubMembers.AddRange(geoGuessrCurrentClubMembers);
        
        // Get hashset of user ids that are current club members
        var clubMemberUserIds = geoGuessrCurrentClubMembers.Select(x => x.UserId).ToHashSet();
        
        // Append the database club members that are not in the GeoGuessr club members
        joinedClubMembers
            .AddRange(databaseClubMembers
                .Where(m => clubMemberUserIds.Contains(m.UserId) == false)
                .Select(m =>
                {
                    // Copy the club member
                    var copy = m.DeepCopy();
                    
                    // Set the is member to false
                    copy.IsCurrentlyMember = false;

                    return copy;
                }));

        return joinedClubMembers;
    }
    
    private readonly Guid _clubId = config.GetValue<Guid>(ConfigKeys.GeoGuessrClubIdConfigurationKey);
}