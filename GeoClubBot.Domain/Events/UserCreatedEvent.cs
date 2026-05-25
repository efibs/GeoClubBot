using MediatR;

namespace Entities.Events;

public record UserCreatedEvent(string UserId, string Nickname) : INotification;
