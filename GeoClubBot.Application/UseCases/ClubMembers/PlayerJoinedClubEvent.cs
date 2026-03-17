using Entities;
using MediatR;

namespace UseCases.UseCases.ClubMembers;

public record PlayerJoinedClubEvent(ClubMember ClubMember) : INotification;