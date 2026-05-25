using Configuration;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.InputPorts.Club;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.GeoGuessr.Assemblers;
using UseCases.UseCases.ClubMembers;

namespace UseCases.UseCases.Club;

public class SyncClubsUseCase(
    IGeoGuessrClientFactory geoGuessrClientFactory,
    IUnitOfWork unitOfWork,
    ISetClubLevelStatusUseCase setClubLevelStatusUseCase,
    ISender mediator,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig) : ISyncClubsUseCase
{
    public async Task SyncClubsAsync()
    {
        var apiSnapshots = new List<ClubMemberSyncSnapshot>();
        var dbMemberUserIds = new HashSet<string>();
        var apiMemberUserIds = new HashSet<string>();

        foreach (var configClub in geoGuessrConfig.Value.Clubs)
        {
            var clubId = configClub.ClubId;
            var client = geoGuessrClientFactory.CreateClient(clubId);

            var clubDto = await client.ReadClubAsync(clubId).ConfigureAwait(false);
            var club = ClubAssembler.AssembleEntity(clubDto);

            await unitOfWork.Clubs.CreateOrUpdateClubAsync(club).ConfigureAwait(false);

            if (clubId == geoGuessrConfig.Value.MainClub.ClubId)
            {
                await setClubLevelStatusUseCase.SetClubLevelStatusAsync(club.Level).ConfigureAwait(false);
            }

            var dbMembers = await unitOfWork.ClubMembers
                .ReadClubMembersByClubIdAsync(clubId)
                .ConfigureAwait(false);
            foreach (var dbMember in dbMembers)
            {
                dbMemberUserIds.Add(dbMember.UserId);

                // Default snapshot puts the member outside the club; if the API still has
                // them, the entry will be replaced below.
                apiSnapshots.Add(new ClubMemberSyncSnapshot(
                    dbMember.UserId, dbMember.User.Nickname, null, dbMember.Xp, dbMember.JoinedAt));
            }

            var apiMembers = ClubMemberAssembler.AssembleEntities(clubDto.Members, clubDto.ClubId);
            foreach (var apiMember in apiMembers)
            {
                apiMemberUserIds.Add(apiMember.UserId);
            }

            // Override the "left" placeholders with real API snapshots for members still present.
            apiSnapshots.RemoveAll(s => apiMembers.Any(m => m.UserId == s.UserId));
            apiSnapshots.AddRange(apiMembers.Select(m =>
                new ClubMemberSyncSnapshot(m.UserId, m.User.Nickname, clubId, m.Xp, m.JoinedAt)));
        }

        await mediator.Send(new SaveClubMembersCommand(apiSnapshots)).ConfigureAwait(false);

        await unitOfWork.SaveChangesAsync().ConfigureAwait(false);
    }
}
