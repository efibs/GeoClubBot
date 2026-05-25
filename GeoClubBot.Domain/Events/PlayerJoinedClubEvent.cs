using MediatR;

namespace Entities.Events;

public record PlayerJoinedClubEvent(
    string UserId,
    string Nickname,
    ulong? DiscordUserId,
    Guid ClubId,
    ulong? PrivateTextChannelId) : INotification;
