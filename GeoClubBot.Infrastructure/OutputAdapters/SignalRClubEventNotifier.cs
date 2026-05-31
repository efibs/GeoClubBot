using Entities;
using Infrastructure.OutputAdapters.Hubs;
using Microsoft.AspNetCore.SignalR;
using UseCases.OutputPorts.Notifications;

namespace Infrastructure.OutputAdapters;

public class SignalRClubEventNotifier(IHubContext<ClubNotificationHub, IClubNotificationClient> hubContext) : IClubEventNotifier
{
    public async Task SendClubLevelUpEvent(Club club, CancellationToken cancellationToken = default)
    {
        await hubContext.Clients.All.ClubLevelUp(club.Level).ConfigureAwait(false);
    }
}
