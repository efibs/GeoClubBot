using MediatR;

namespace Entities.Events;

public record UserUpdatedEvent(
    string UserId,
    string OldNickname,
    string NewNickname,
    ulong? OldDiscordUserId,
    ulong? NewDiscordUserId) : INotification;
