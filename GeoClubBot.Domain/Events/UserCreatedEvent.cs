using MediatR;

namespace Entities.Events;

public record UserCreatedEvent(GeoGuessrUser CreatedUser) : INotification;
