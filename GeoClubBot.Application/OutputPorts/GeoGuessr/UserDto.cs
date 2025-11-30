namespace UseCases.OutputPorts.GeoGuessr;

public class UserDto
{
    public required string Nick { get; set; }

    public required DateTimeOffset Created { get; set; }

    public required bool IsProUser { get; set; }

    public required string Type { get; set; }

    public required bool IsVerified { get; set; }

    public required string CustomImage { get; set; }

    public required string FullBodyPin { get; set; }

    public required string BorderUrl { get; set; }

    public required int Color { get; set; }

    public required string Url { get; set; }

    public required string Id { get; set; }

    public required string CountryCode { get; set; }

    public required UserCompetitiveDto Competitive { get; set; }

    public required bool IsBanned { get; set; }

    public required bool ChatBan { get; set; }
}