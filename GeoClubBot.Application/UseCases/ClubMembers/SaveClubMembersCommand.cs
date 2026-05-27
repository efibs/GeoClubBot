using Entities;
using MediatR;
using UseCases.Abstractions;
using UseCases.OutputPorts;

namespace UseCases.UseCases.ClubMembers;

public sealed record SaveClubMembersCommand(IReadOnlyList<ClubMemberSyncSnapshot> Snapshots) : ICommand;

public sealed class SaveClubMembersHandler(
    IGeoGuessrUserRepository users,
    IClubMemberRepository members) : IRequestHandler<SaveClubMembersCommand, Unit>
{
    public async Task<Unit> Handle(SaveClubMembersCommand request, CancellationToken cancellationToken)
    {
        foreach (var snapshot in request.Snapshots)
        {
            var persistedUser = await UpsertUserAsync(snapshot, cancellationToken).ConfigureAwait(false);
            await UpsertMemberAsync(snapshot, persistedUser, cancellationToken).ConfigureAwait(false);
        }

        return Unit.Value;
    }

    private async Task<GeoGuessrUser> UpsertUserAsync(ClubMemberSyncSnapshot snapshot, CancellationToken cancellationToken)
    {
        var existing = await users.ReadForUpdateByUserIdAsync(snapshot.UserId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            existing.UpdateFromApi(snapshot.Nickname);
            return existing;
        }

        var created = GeoGuessrUser.Create(snapshot.UserId, snapshot.Nickname);
        users.AddUser(created);
        return created;
    }

    private async Task UpsertMemberAsync(ClubMemberSyncSnapshot snapshot, GeoGuessrUser persistedUser, CancellationToken cancellationToken)
    {
        var existing = await members.ReadForUpdateByUserIdAsync(snapshot.UserId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            existing.SyncFromApi(snapshot.TargetClubId, snapshot.Xp, snapshot.JoinedAt);
            return;
        }

        var newMember = ClubMember.Create(persistedUser, snapshot.TargetClubId, snapshot.Xp, snapshot.JoinedAt);
        members.AddClubMember(newMember);
    }
}
