using Entities;
using MediatR;

namespace UseCases.UseCases.Users;

public record UserCreatedEvent(GeoGuessrUser CreatedUser) : INotification;