using Entities;
using MediatR;

namespace UseCases.UseCases.ClubMembers;

public record PlayerSwitchedClubsEvent(ClubMember OldClubMember, ClubMember NewClubMember) : INotification;