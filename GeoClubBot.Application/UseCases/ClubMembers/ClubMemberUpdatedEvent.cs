using Entities;
using MediatR;

namespace UseCases.UseCases.ClubMembers;

public record ClubMemberUpdatedEvent(ClubMember OldClubMember, ClubMember NewClubMember) : INotification;