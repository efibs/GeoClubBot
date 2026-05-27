using Configuration;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.GeoGuessr.Assemblers;
using UseCases.UseCases.ClubMembers;

namespace UseCases.UseCases.Club;

public sealed record SyncClubsCommand : ICommand;

public sealed class SyncClubsHandler(
    IGeoGuessrClientFactory geoGuessrClientFactory,
    IClubRepository clubs,
    IClubMemberRepository clubMembers,
    ISender mediator,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig) : IRequestHandler<SyncClubsCommand, Unit>
{
    public async Task<Unit> Handle(SyncClubsCommand request, CancellationToken cancellationToken)
    {
        var apiSnapshots = new List<ClubMemberSyncSnapshot>();

        foreach (var configClub in geoGuessrConfig.Value.Clubs)
        {
            var clubId = configClub.ClubId;
            var client = geoGuessrClientFactory.CreateClient(clubId);

            var clubDto = await client.ReadClubAsync(clubId, cancellationToken).ConfigureAwait(false);
            var club = ClubAssembler.AssembleEntity(clubDto);

            await clubs.CreateOrUpdateClubAsync(club, cancellationToken).ConfigureAwait(false);

            if (clubId == geoGuessrConfig.Value.MainClub.ClubId)
            {
                await mediator.Send(new SetClubLevelStatusCommand(club.Level), cancellationToken).ConfigureAwait(false);
            }

            var dbMembers = await clubMembers
                .ReadClubMembersByClubIdAsync(clubId, cancellationToken)
                .ConfigureAwait(false);
            foreach (var dbMember in dbMembers)
            {
                // Default snapshot puts the member outside the club; if the API still has
                // them, the entry will be replaced below.
                apiSnapshots.Add(new ClubMemberSyncSnapshot(
                    dbMember.UserId, dbMember.User.Nickname, null, dbMember.Xp, dbMember.JoinedAt));
            }

            var apiMembers = ClubMemberAssembler.AssembleEntities(clubDto.Members, clubDto.ClubId);
            apiSnapshots.RemoveAll(s => apiMembers.Any(m => m.UserId == s.UserId));
            apiSnapshots.AddRange(apiMembers.Select(m =>
                new ClubMemberSyncSnapshot(m.UserId, m.User.Nickname, clubId, m.Xp, m.JoinedAt)));
        }

        await mediator.Send(new SaveClubMembersCommand(apiSnapshots), cancellationToken).ConfigureAwait(false);

        return Unit.Value;
    }
}
