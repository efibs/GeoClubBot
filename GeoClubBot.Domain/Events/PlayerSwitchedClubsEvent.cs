using MediatR;

namespace Entities.Events;

public record PlayerSwitchedClubsEvent(
    string UserId,
    string Nickname,
    ulong? DiscordUserId,
    Guid OldClubId,
    Guid NewClubId) : INotification;
