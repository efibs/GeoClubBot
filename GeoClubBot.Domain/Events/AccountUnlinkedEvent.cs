using MediatR;

namespace Entities.Events;

public record AccountUnlinkedEvent(string UserId, string Nickname, ulong DiscordUserId) : INotification;
