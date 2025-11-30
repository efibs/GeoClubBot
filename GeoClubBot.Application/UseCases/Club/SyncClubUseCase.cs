using Constants;
using Entities;
using Microsoft.Extensions.Configuration;
using UseCases.InputPorts.Club;
using UseCases.InputPorts.ClubMembers;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.GeoGuessr.Assemblers;

namespace UseCases.UseCases.Club;

public class SyncClubUseCase(
    IGeoGuessrClient geoGuessrClient,
    IUnitOfWork unitOfWork,
    ISetClubLevelStatusUseCase setClubLevelStatusUseCase,
    ISaveClubMembersUseCase saveClubMembersUseCase,
    IConfiguration config) : ISyncClubUseCase
{
    public async Task SyncClubAsync()
    {
        // Read the GeoGuessr club
        var clubDto = await geoGuessrClient.ReadClubAsync(_clubId).ConfigureAwait(false);
        
        // Assemble the entity
        var club = ClubAssembler.AssembleEntity(clubDto);
        
        // Sync the club
        await unitOfWork.Clubs.CreateOrUpdateClubAsync(club).ConfigureAwait(false);
        
        // Set the status
        await setClubLevelStatusUseCase.SetClubLevelStatusAsync(club.Level).ConfigureAwait(false);
        
        // Read the members from the database
        var databaseClubMembers = await unitOfWork.ClubMembers.ReadClubMembersAsync().ConfigureAwait(false);
        
        // Assemble the club members
        var geoGuessrClubMembers = ClubMemberAssembler.AssembleEntities(clubDto.Members, clubDto.ClubId);
        
        // Join the two lists
        var toSaveClubMembers = _joinClubMembersList(geoGuessrClubMembers, databaseClubMembers);
        
        // Save the club members
        await saveClubMembersUseCase.SaveClubMembersAsync(toSaveClubMembers).ConfigureAwait(false);
        
        // Save
        await unitOfWork.SaveChangesAsync().ConfigureAwait(false);
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
                    // Set the is member to false
                    m.IsCurrentlyMember = false;

                    return m;
                }));

        return joinedClubMembers;
    }
    
    private readonly Guid _clubId = config.GetValue<Guid>(ConfigKeys.GeoGuessrClubIdConfigurationKey);
}