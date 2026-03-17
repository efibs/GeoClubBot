using System.Collections.Concurrent;
using Configuration;
using Entities;
using GeoClubBot.MockGeoGuessr.DataStore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;

namespace GeoClubBot.MockGeoGuessr.Initialization;

public class MockGeoGuessrDataInitializer(
    IServiceProvider serviceProvider,
    MockGeoGuessrDataStore dataStore,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    ILogger<MockGeoGuessrDataInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Initializing mock GeoGuessr data from database...");

        using var scope = serviceProvider.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        foreach (var clubEntry in geoGuessrConfig.Value.Clubs)
        {
            var dbClub = await unitOfWork.Clubs.ReadClubByIdAsync(clubEntry.ClubId);

            if (dbClub is null)
            {
                logger.LogWarning("Club {ClubId} not found in database, creating stub", clubEntry.ClubId);
                CreateStubClub(clubEntry.ClubId);
                continue;
            }

            dataStore.Clubs[dbClub.ClubId] = MapClubToDto(dbClub);

            var dbMembers = await unitOfWork.ClubMembers.ReadClubMembersByClubIdAsync(dbClub.ClubId);
            var memberDict = new ConcurrentDictionary<string, ClubMemberDto>();

            foreach (var dbMember in dbMembers.Where(m => m.IsCurrentlyMember))
            {
                memberDict[dbMember.UserId] = MapClubMemberToDto(dbMember);
                dataStore.Users.TryAdd(dbMember.UserId, MapGeoGuessrUserToDto(dbMember.User));
            }

            dataStore.ClubMembers[dbClub.ClubId] = memberDict;
            dataStore.ClubActivities[dbClub.ClubId] = [];
        }

        // Load linked users that may not be in any club currently
        var allLinkedUsers = await unitOfWork.GeoGuessrUsers.ReadAllLinkedUsersAsync();
        foreach (var user in allLinkedUsers)
            dataStore.Users.TryAdd(user.UserId, MapGeoGuessrUserToDto(user));

        // Load existing challenges
        var challengeLinks = await unitOfWork.ClubChallenges.ReadLatestClubChallengeLinksAsync();
        foreach (var link in challengeLinks)
        {
            dataStore.Challenges.TryAdd(link.ChallengeId, new PostChallengeRequestDto
            {
                AccessLevel = 0,
                ChallengeType = 0,
                ForbidMoving = false,
                ForbidRotating = false,
                ForbidZooming = false,
                Map = "world",
                TimeLimit = 60
            });
            dataStore.ChallengeHighscores.TryAdd(link.ChallengeId, []);
        }

        logger.LogInformation(
            "Mock GeoGuessr data initialized: {ClubCount} clubs, {UserCount} users, {ChallengeCount} challenges",
            dataStore.Clubs.Count, dataStore.Users.Count, dataStore.Challenges.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static ClubDto MapClubToDto(Club club) => new()
    {
        ClubId = club.ClubId,
        Name = club.Name,
        Level = club.Level,
        Xp = 0,
        Members = [],
        MemberCount = 0,
        MaxMemberCount = 30,
        JoinRule = 0,
        Tag = club.Name.Length >= 3 ? club.Name[..3].ToUpperInvariant() : club.Name.ToUpperInvariant(),
        Description = $"Mock club: {club.Name}",
        CreatedAt = DateTimeOffset.UtcNow.AddYears(-1),
        Language = "en",
        Labels = [],
        BackgroundUrl = "",
        Stats = CreateDefaultStats(club.ClubId)
    };

    private static ClubMemberDto MapClubMemberToDto(ClubMember member) => new()
    {
        User = new ClubMemberUserDto
        {
            UserId = member.UserId,
            Nick = member.User.Nickname,
            Avatar = "",
            FullBodyAvatar = "",
            BorderUrl = "",
            IsVerified = false,
            Flair = 0,
            CountryCode = "us",
            TierId = 0,
            ClubUserType = 0
        },
        Role = 0,
        JoinedAt = member.JoinedAt,
        IsOnline = false,
        Xp = member.Xp,
        WeeklyXp = 0,
        LastActive = null
    };

    private static UserDto MapGeoGuessrUserToDto(GeoGuessrUser user) => new()
    {
        Id = user.UserId,
        Nick = user.Nickname,
        Created = DateTimeOffset.UtcNow.AddYears(-1),
        IsProUser = true,
        Type = "user",
        IsVerified = false,
        CustomImage = "",
        FullBodyPin = "",
        BorderUrl = "",
        Color = 0,
        Url = $"/user/{user.UserId}",
        CountryCode = "us",
        Competitive = new UserCompetitiveDto
        {
            Elo = 1000,
            Rating = 1000,
            LastRatingChange = 0
        },
        IsBanned = false,
        ChatBan = false
    };

    private void CreateStubClub(Guid clubId)
    {
        dataStore.Clubs[clubId] = new ClubDto
        {
            ClubId = clubId,
            Name = $"Club {clubId.ToString()[..8]}",
            Level = 1,
            Xp = 0,
            Members = [],
            MemberCount = 0,
            MaxMemberCount = 30,
            JoinRule = 0,
            Tag = "MCK",
            Description = "Auto-generated mock club",
            CreatedAt = DateTimeOffset.UtcNow,
            Language = "en",
            Labels = [],
            BackgroundUrl = "",
            Stats = CreateDefaultStats(clubId)
        };
        dataStore.ClubMembers[clubId] = new ConcurrentDictionary<string, ClubMemberDto>();
        dataStore.ClubActivities[clubId] = [];
    }

    private static ClubStatsDto CreateDefaultStats(Guid clubId) => new()
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
        GlobalXpRank = 1,
        TotalClubs = 1,
        AverageDivision = new ClubAverageDivisionDto
        {
            Number = 1,
            Name = "Bronze",
            Tier = 1
        }
    };
}
