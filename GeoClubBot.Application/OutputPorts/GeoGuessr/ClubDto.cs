namespace UseCases.OutputPorts.GeoGuessr;

public class ClubDto
{
    public required Guid ClubId { get; set; }

    public required string Name { get; set; }

    public required List<ClubMemberDto> Members { get; set; }

    public required int JoinRule { get; set; }

    public required string Tag { get; set; }

    public required string Description { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    public required string Language { get; set; }

    public required int MemberCount { get; set; }

    public required int MaxMemberCount { get; set; }

    public required int Level { get; set; }

    public required int Xp { get; set; }

    public required List<string> Labels { get; set; }

    public required ClubStatsDto Stats { get; set; }

    public required string BackgroundUrl { get; set; }
}