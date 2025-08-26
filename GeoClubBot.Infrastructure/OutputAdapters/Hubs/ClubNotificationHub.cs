using UseCases.OutputPorts;
using Microsoft.AspNetCore.SignalR;

namespace Infrastructure.OutputAdapters.Hubs;

public interface IClubNotificationClient
{
    Task ClubLevelUp(int newLevel);
}

public sealed class ClubNotificationHub : Hub<IClubNotificationClient>
{
}