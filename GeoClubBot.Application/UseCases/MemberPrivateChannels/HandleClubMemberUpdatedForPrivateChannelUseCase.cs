using MediatR;
using UseCases.UseCases.ClubMembers;

namespace UseCases.UseCases.MemberPrivateChannels;

public class HandleClubMemberUpdatedForPrivateChannelUseCase : INotificationHandler<ClubMemberUpdatedEvent>
{
    public Task Handle(ClubMemberUpdatedEvent notification, CancellationToken cancellationToken)
    {
        // TODO: If account link changed: Create or delete channel
        //throw new NotImplementedException();
        return Task.CompletedTask;
    }
}