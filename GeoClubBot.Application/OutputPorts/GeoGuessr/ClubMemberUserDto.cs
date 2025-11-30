namespace UseCases.OutputPorts.GeoGuessr;

public class ClubMemberUserDto
{
    public required string UserId { get; set; }

    public required string Nick { get; set; }

    public required string Avatar { get; set; }

    public required string FullBodyAvatar { get; set; }

    public required string BorderUrl { get; set; }

    public required bool IsVerified { get; set; }

    public required int Flair { get; set; }

    public required string CountryCode { get; set; }

    public required int TierId { get; set; }

    public required int ClubUserType { get; set; }
}