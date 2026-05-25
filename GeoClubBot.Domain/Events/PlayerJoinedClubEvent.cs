using MediatR;

namespace Entities.Events;

public record PlayerJoinedClubEvent(ClubMember ClubMember) : INotification;
