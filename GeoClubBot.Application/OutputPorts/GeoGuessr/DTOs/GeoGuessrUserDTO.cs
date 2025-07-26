namespace Entities;

public record GeoGuessrUserDTO(
    string UserId,
    string Nick,
    string Avatar,
    string FullbodyAvatar,
    bool IsVerified,
    int Flair,
    string CountryCode,
    int TierId,
    int ClubUserType);