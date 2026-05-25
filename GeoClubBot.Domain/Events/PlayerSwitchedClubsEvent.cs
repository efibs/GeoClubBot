using MediatR;

namespace Entities.Events;

public record PlayerSwitchedClubsEvent(ClubMember OldClubMember, ClubMember NewClubMember) : INotification;
