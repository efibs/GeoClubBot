using Configuration;
using Entities;
using Microsoft.Extensions.Options;
using UseCases.InputPorts.Club;
using UseCases.InputPorts.ClubMembers;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.GeoGuessr.Assemblers;

namespace UseCases.UseCases.Club;

public class SyncClubUseCase(
    IGeoGuessrClientFactory geoGuessrClientFactory,
    IUnitOfWork unitOfWork,
    ISetClubLevelStatusUseCase setClubLevelStatusUseCase,
    ISaveClubMembersUseCase saveClubMembersUseCase,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig) : ISyncClubUseCase
{
    public async Task SyncClubAsync(Guid clubId)
    {
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
        var databaseClubMembers = await unitOfWork.ClubMembers.ReadClubMembersByClubIdAsync(clubId).ConfigureAwait(false);

        // Assemble the club members
        var geoGuessrClubMembers = ClubMemberAssembler.AssembleEntities(clubDto.Members, clubDto.ClubId);

        // Join the two lists
        var toSaveClubMembers = _joinClubMembersList(geoGuessrClubMembers, databaseClubMembers, clubId);

        // Save the club members
        await saveClubMembersUseCase.SaveClubMembersAsync(toSaveClubMembers).ConfigureAwait(false);

        // Save
        await unitOfWork.SaveChangesAsync().ConfigureAwait(false);
    }

    private List<ClubMember> _joinClubMembersList(List<ClubMember> geoGuessrCurrentClubMembers,
        IEnumerable<ClubMember> databaseClubMembers,
        Guid clubId)
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

        // Update the club id on all actual club members
        foreach (var member in joinedClubMembers
                     .Where(m => clubMemberUserIds.Contains(m.UserId)))
        {
            // Set the club id
            member.ClubId = clubId;
        }
        
        return joinedClubMembers;
    }
}
