using MediatR;

namespace Entities.Events;

public record PlayerLeftClubEvent(ClubMember ClubMember) : INotification;
