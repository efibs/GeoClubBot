using Entities;
using MediatR;

namespace UseCases.UseCases.Users;

public record UserUpdatedEvent(GeoGuessrUser OldUser, GeoGuessrUser NewUser) : INotification;