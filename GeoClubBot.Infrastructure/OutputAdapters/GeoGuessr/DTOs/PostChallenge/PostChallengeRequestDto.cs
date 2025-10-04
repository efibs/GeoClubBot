namespace Infrastructure.OutputAdapters.GeoGuessr.DTOs.PostChallenge;

public record PostChallengeRequestDto(
    int AccessLevel,
    int ChallengeType,
    bool ForbidMoving,
    bool ForbidRotating,
    bool ForbidZooming,
    string Map,
    int TimeLimit);