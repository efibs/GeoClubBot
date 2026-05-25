using MediatR;

namespace Entities.Events;

public record PlayerLeftClubEvent(
    string UserId,
    string Nickname,
    ulong? DiscordUserId,
    Guid OldClubId,
    ulong? PrivateTextChannelId) : INotification;
