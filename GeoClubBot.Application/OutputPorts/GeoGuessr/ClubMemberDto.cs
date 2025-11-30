namespace UseCases.OutputPorts.GeoGuessr;

public class ClubMemberDto
{
    public required ClubMemberUserDto User { get; set; }
    
    public required int Role { get; set; }

    public required DateTimeOffset JoinedAt { get; set; }

    public bool? IsOnline { get; set; } = null;

    public required int Xp { get; set; }

    public required int WeeklyXp { get; set; }

    public DateTimeOffset? LastActive { get; set; } = null;
}