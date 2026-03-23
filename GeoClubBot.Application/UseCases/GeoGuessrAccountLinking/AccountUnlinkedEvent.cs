using Entities;
using MediatR;

namespace UseCases.UseCases.GeoGuessrAccountLinking;

public record AccountUnlinkedEvent(GeoGuessrUser User) : INotification;