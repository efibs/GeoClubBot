using Entities;
using MediatR;

namespace UseCases.UseCases.GeoGuessrAccountLinking;

public record AccountLinkedEvent(GeoGuessrUser User) : INotification;