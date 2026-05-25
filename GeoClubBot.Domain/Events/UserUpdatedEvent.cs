using MediatR;

namespace Entities.Events;

public record UserUpdatedEvent(GeoGuessrUser OldUser, GeoGuessrUser NewUser) : INotification;
