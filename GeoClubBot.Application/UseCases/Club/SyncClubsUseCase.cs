using Configuration;
using Entities;
using Microsoft.Extensions.Options;
using UseCases.InputPorts.Club;
using UseCases.InputPorts.ClubMembers;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.GeoGuessr.Assemblers;

namespace UseCases.UseCases.Club;

public class SyncClubsUseCase(
    IGeoGuessrClientFactory geoGuessrClientFactory,
    IUnitOfWork unitOfWork,
    ISetClubLevelStatusUseCase setClubLevelStatusUseCase,
    ISaveClubMembersUseCase saveClubMembersUseCase,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig) : ISyncClubsUseCase
{
    public async Task SyncClubsAsync()
    {
        var geoGuessrClubMembers = new List<ClubMember>();
        var databaseClubMembers = new List<ClubMember>();
        
        // For every club
        foreach (var configClub in geoGuessrConfig.Value.Clubs)
        {
            // Get the club id
            var clubId = configClub.ClubId;
            
            // Get the client for this club
            var client = geoGuessrClientFactory.CreateClient(clubId);

            // Read the GeoGuessr club
            var clubDto = await client.ReadClubAsync(clubId).ConfigureAwait(false);
            
            // Assemble the entity
            var club = ClubAssembler.AssembleEntity(clubDto);
            
            // Sync the club
            await unitOfWork.Clubs.CreateOrUpdateClubAsync(club).ConfigureAwait(false);
            
            // Only set bot status for the main club
            if (clubId == geoGuessrConfig.Value.MainClub.ClubId)
            {
                await setClubLevelStatusUseCase.SetClubLevelStatusAsync(club.Level).ConfigureAwait(false);
            }
            
            // Read the members from the database for this club
            var databaseClubMembersCurrentClub =
                await unitOfWork.ClubMembers.ReadClubMembersByClubIdAsync(clubId).ConfigureAwait(false);
            
            // Add to all db club members
            databaseClubMembers.AddRange(databaseClubMembersCurrentClub);
            
            // Assemble the club members
            var geoGuessrClubMembersCurrentClub = ClubMemberAssembler.AssembleEntities(clubDto.Members, clubDto.ClubId);

            // Add to all gg club members
            geoGuessrClubMembers.AddRange(geoGuessrClubMembersCurrentClub);
        }

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
                    // Set the club id to null
                    m.ClubId = null;
                    
                    return m;
                }));
        
        return joinedClubMembers;
    }
}
