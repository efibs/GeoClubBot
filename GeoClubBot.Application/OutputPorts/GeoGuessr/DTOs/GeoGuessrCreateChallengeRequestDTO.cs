namespace UseCases.OutputPorts.GeoGuessr.DTOs;

public record GeoGuessrCreateChallengeRequestDTO(
    int AccessLevel,
    int ChallengeType,
    bool ForbidMoving,
    bool ForbidRotating,
    bool ForbidZooming,
    string Map,
    int TimeLimit);