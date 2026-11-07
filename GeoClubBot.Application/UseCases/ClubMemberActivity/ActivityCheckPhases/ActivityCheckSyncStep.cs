using Entities;
using MediatR;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.GeoGuessr.Assemblers;
using UseCases.UseCases.ClubMembers;
using UseCases.UseCases.Strikes;

namespace UseCases.UseCases.ClubMemberActivity.ActivityCheckPhases;

/// <summary>
/// First phase of <see cref="CheckGeoGuessrPlayerActivityHandler"/>: prune decayed strikes,
/// fetch the API roster for the club, and upsert the persisted members in one round-trip.
/// </summary>
public sealed class ActivityCheckSyncStep(
    IGeoGuessrClientFactory geoGuessrClientFactory,
    ISender mediator,
    IClubMemberSyncGate memberSyncGate)
{
    public async Task<List<ClubMember>> ExecuteAsync(Guid clubId, CancellationToken cancellationToken)
    {
        await mediator.Send(new CheckStrikeDecayCommand(), cancellationToken).ConfigureAwait(false);

        // From the roster read to the end of the save, so a leave pushed in between isn't undone by
        // this older roster.
        await using (await memberSyncGate.AcquireAsync(cancellationToken).ConfigureAwait(false))
        {
            var client = geoGuessrClientFactory.CreateClient(clubId);
            var response = await client.ReadClubMembersAsync(clubId, cancellationToken).ConfigureAwait(false);
            var members = ClubMemberAssembler.AssembleEntities(response, clubId);

            var snapshots = members
                .Select(m => new ClubMemberSyncSnapshot(m.UserId, m.User.Nickname, clubId, m.Xp, m.JoinedAt))
                .ToList();
            await mediator.Send(new SaveClubMembersCommand(snapshots), cancellationToken).ConfigureAwait(false);

            return members;
        }
    }
}
