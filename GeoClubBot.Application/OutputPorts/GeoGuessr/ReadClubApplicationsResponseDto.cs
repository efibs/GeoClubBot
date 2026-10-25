namespace UseCases.OutputPorts.GeoGuessr;

/// <summary>
/// A page of a club's pending applications. Each item is the applicant in the same shape as a
/// member's user; GeoGuessr sends no application id, date or message.
/// </summary>
public class ReadClubApplicationsResponseDto
{
    public required List<ClubMemberUserDto> Items { get; set; }

    public string? PaginationToken { get; set; }
}
