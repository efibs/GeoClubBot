using UseCases.OutputPorts.GeoGuessr;

namespace GeoClubBot.Tests.TestBuilders;

/// <summary>GeoGuessr DTOs with their many required properties filled in.</summary>
public static class GeoGuessrDtos
{
    public static ClubMemberDto Member(string userId, string nick, int xp = 0, int weeklyXp = 0) => new()
    {
        User = new ClubMemberUserDto
        {
            UserId = userId,
            Nick = nick,
            Avatar = string.Empty,
            FullBodyAvatar = string.Empty,
            BorderUrl = string.Empty,
            IsVerified = false,
            Flair = 0,
            CountryCode = "de",
            TierId = 0,
            ClubUserType = 0
        },
        Role = 0,
        JoinedAt = DateTimeOffset.UnixEpoch,
        Xp = xp,
        WeeklyXp = weeklyXp
    };

    public static ClubDto Club(Guid clubId, int level = 1, string name = "Club") => new()
    {
        ClubId = clubId,
        Name = name,
        Members = [],
        JoinRule = 0,
        Tag = "TAG",
        Description = string.Empty,
        CreatedAt = DateTimeOffset.UnixEpoch,
        Language = "en",
        MemberCount = 0,
        MaxMemberCount = 30,
        Level = level,
        Xp = 0,
        Labels = [],
        Stats = new ClubStatsDto
        {
            ClubId = clubId,
            TotalXp = 0,
            ChangePercentXp = 0,
            TotalGamesPlayed = 0,
            ChangePercentGamesPlayed = 0,
            TotalWins = 0,
            ChangePercentWins = 0,
            TotalPerfectGuesses = 0,
            ChangePercentPerfectGuesses = 0,
            GlobalXpRank = 0,
            TotalClubs = 0,
            AverageDivision = new ClubAverageDivisionDto { Number = 1, Name = "Gold", Tier = 1 }
        },
        BackgroundUrl = string.Empty
    };
}
