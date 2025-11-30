namespace UseCases.OutputPorts.GeoGuessr;

public class PostChallengeRequestDto
{
    public required int AccessLevel { get; set; }
    
    public required int ChallengeType { get; set; }
    
    public required bool ForbidMoving { get; set; }
    
    public required bool ForbidRotating { get; set; }
    
    public required bool ForbidZooming { get; set; }
    
    public required string Map { get; set; }
    
    public required int TimeLimit { get; set; }
}