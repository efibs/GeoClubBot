namespace UseCases.OutputPorts.GeoGuessr;

public class UserClubDto
{
    public required string Tag { get; set; }
    public required Guid ClubId { get; set; }
    public required int Level { get; set; }
}
