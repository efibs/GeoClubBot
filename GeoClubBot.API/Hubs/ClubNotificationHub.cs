using Microsoft.AspNetCore.SignalR;

namespace GeoClubBot.Hubs;

public interface IClubNotificationClient
{
    Task ClubLevelUp(int newLevel);
}

public class ClubNotificationHub : Hub<IClubNotificationClient>
{
}