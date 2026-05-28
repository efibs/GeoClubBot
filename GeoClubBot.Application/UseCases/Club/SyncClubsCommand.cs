using Configuration;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
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
    IServiceScopeFactory scopeFactory,
    ISender mediator,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig) : IRequestHandler<SyncClubsCommand, Unit>
{
    public async Task<Unit> Handle(SyncClubsCommand request, CancellationToken cancellationToken)
    {
        var mainClubId = geoGuessrConfig.Value.MainClub.ClubId;

        var perClubSnapshots = await Task.WhenAll(geoGuessrConfig.Value.Clubs.Select(async configClub =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var scopedSender = scope.ServiceProvider.GetRequiredService<ISender>();
            var scopedClubs = scope.ServiceProvider.GetRequiredService<IClubRepository>();
            var scopedClubMembers = scope.ServiceProvider.GetRequiredService<IClubMemberRepository>();
            var scopedUnitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var clubId = configClub.ClubId;
            var client = geoGuessrClientFactory.CreateClient(clubId);

            var clubDto = await client.ReadClubAsync(clubId, cancellationToken).ConfigureAwait(false);
            var club = ClubAssembler.AssembleEntity(clubDto);

            await scopedClubs.CreateOrUpdateClubAsync(club, cancellationToken).ConfigureAwait(false);
            await scopedUnitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (clubId == mainClubId)
            {
                await scopedSender
                    .Send(new SetClubLevelStatusCommand(club.Level), cancellationToken)
                    .ConfigureAwait(false);
            }

            var dbMembers = await scopedClubMembers
                .ReadClubMembersByClubIdAsync(clubId, cancellationToken)
                .ConfigureAwait(false);
            var apiMembers = ClubMemberAssembler.AssembleEntities(clubDto.Members, clubDto.ClubId);

            var apiUserIds = apiMembers.Select(m => m.UserId).ToHashSet();
            var clubSnapshots = new List<ClubMemberSyncSnapshot>(dbMembers.Count + apiMembers.Count);
            clubSnapshots.AddRange(dbMembers
                .Where(m => !apiUserIds.Contains(m.UserId))
                .Select(m => new ClubMemberSyncSnapshot(m.UserId, m.User.Nickname, null, m.Xp, m.JoinedAt)));
            clubSnapshots.AddRange(apiMembers.Select(m =>
                new ClubMemberSyncSnapshot(m.UserId, m.User.Nickname, clubId, m.Xp, m.JoinedAt)));
            return clubSnapshots;
        })).ConfigureAwait(false);

        // Merge across clubs. If a user appears in one club's API ("in club X") but was a DB
        // member of a different club, drop their "out of club" placeholder so the in-club
        // entry wins — matching the original sequential semantics.
        var allSnapshots = perClubSnapshots.SelectMany(s => s).ToList();
        var inClubUserIds = allSnapshots
            .Where(s => s.TargetClubId is not null)
            .Select(s => s.UserId)
            .ToHashSet();
        var finalSnapshots = allSnapshots
            .Where(s => s.TargetClubId is not null || !inClubUserIds.Contains(s.UserId))
            .ToList();

        await mediator.Send(new SaveClubMembersCommand(finalSnapshots), cancellationToken).ConfigureAwait(false);

        return Unit.Value;
    }
}
