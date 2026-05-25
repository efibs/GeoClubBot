using MediatR;

namespace Entities.Events;

public record AccountLinkedEvent(string UserId, string Nickname, ulong DiscordUserId) : INotification;
