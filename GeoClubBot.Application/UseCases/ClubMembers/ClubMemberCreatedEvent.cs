using Entities;
using MediatR;

namespace UseCases.UseCases.ClubMembers;

public record ClubMemberCreatedEvent(ClubMember ClubMember) : INotification;