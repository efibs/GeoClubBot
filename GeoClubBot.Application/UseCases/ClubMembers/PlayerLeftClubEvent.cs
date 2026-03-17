using Entities;
using MediatR;

namespace UseCases.UseCases.ClubMembers;

public record PlayerLeftClubEvent(ClubMember ClubMember) : INotification;