namespace Infrastructure.OutputAdapters.GeoGuessr.DTOs.GetClubMembers;

public record ClubMemberDto(UserDto User, int Xp, DateTimeOffset JoinedAt);