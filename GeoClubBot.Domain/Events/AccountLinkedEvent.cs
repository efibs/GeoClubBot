using MediatR;

namespace Entities.Events;

public record AccountLinkedEvent(GeoGuessrUser User) : INotification;
