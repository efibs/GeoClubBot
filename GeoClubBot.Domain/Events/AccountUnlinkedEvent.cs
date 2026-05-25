using MediatR;

namespace Entities.Events;

public record AccountUnlinkedEvent(GeoGuessrUser User) : INotification;
