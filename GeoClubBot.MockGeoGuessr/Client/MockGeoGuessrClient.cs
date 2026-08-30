using System.Net;
using GeoClubBot.MockGeoGuessr.DataStore;
using UseCases.OutputPorts.GeoGuessr;

namespace GeoClubBot.MockGeoGuessr.Client;

public class MockGeoGuessrClient(MockGeoGuessrDataStore dataStore) : IGeoGuessrClient
{
    public Task<List<ClubMemberDto>> ReadClubMembersAsync(Guid clubId, CancellationToken cancellationToken = default)
    {
        if (dataStore.ClubMembers.TryGetValue(clubId, out var members))
            return Task.FromResult(members.Values.ToList());

        return Task.FromResult(new List<ClubMemberDto>());
    }

    public Task<ClubDto> ReadClubAsync(Guid clubId, CancellationToken cancellationToken = default)
    {
        if (!dataStore.Clubs.TryGetValue(clubId, out var club))
            throw new HttpRequestException($"Club {clubId} not found", null, HttpStatusCode.NotFound);

        if (dataStore.ClubMembers.TryGetValue(clubId, out var members))
        {
            club.Members = members.Values.ToList();
            club.MemberCount = club.Members.Count;
        }

        return Task.FromResult(club);
    }

    public Task<UserDto> ReadUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (!dataStore.Users.TryGetValue(userId, out var user))
            throw new HttpRequestException($"User {userId} not found", null, HttpStatusCode.NotFound);

        return Task.FromResult(user);
    }

    public Task<PostChallengeResponseDto> CreateChallengeAsync(PostChallengeRequestDto request, CancellationToken cancellationToken = default)
    {
        var token = dataStore.GenerateChallengeToken();
        dataStore.Challenges[token] = request;
        dataStore.ChallengeHighscores[token] = [];
        dataStore.NotifyDataChanged();
        return Task.FromResult(new PostChallengeResponseDto { Token = token });
    }

    public Task<ChallengeResultHighscoresDto> ReadHighscoresAsync(string challengeId, ReadHighscoresQueryParams @params, CancellationToken cancellationToken = default)
    {
        var items = new List<ChallengeResultItemDto>();

        if (dataStore.ChallengeHighscores.TryGetValue(challengeId, out var scores))
            items = scores.Take(@params.Limit).ToList();

        return Task.FromResult(new ChallengeResultHighscoresDto { Items = items });
    }

    public Task<DailyMissionsResponseDto> ReadDailyMissionsAsync(CancellationToken cancellationToken = default)
    {
        List<DailyMissionDto> snapshot;
        lock (dataStore.DailyMissions)
        {
            snapshot = dataStore.DailyMissions.ToList();
        }

        return Task.FromResult(new DailyMissionsResponseDto
        {
            Missions = snapshot,
            NextMissionDate = dataStore.NextMissionDate
        });
    }

    public Task<RankedProgressResponseDto> ReadRankedProgressOfUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (dataStore.RankedProgress.TryGetValue(userId, out var progress))
            return Task.FromResult(progress);

        return Task.FromResult(new RankedProgressResponseDto
        {
            DivisionNumber = 1,
            DivisionName = "Bronze",
            Rating = 1000,
            Tier = "Bronze",
            GameModeRatings = new GameModeRatingsDto { MoveDuels = 1000, NoMoveDuels = 1000, NmpzDuels = 1000 },
            GuessedFirstRate = 0.5f,
            WinStreak = 0,
            LatestGames = [],
            BestCountries = [],
            WorstCountries = []
        });
    }

    public Task<RankedPeakRatingResponseDto> ReadRankedPeakRatingOfUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (dataStore.RankedPeakRatings.TryGetValue(userId, out var peakRating))
            return Task.FromResult(peakRating);

        return Task.FromResult(new RankedPeakRatingResponseDto
        {
            PeakOverallRating = 1000,
            PeakGameModeRatings = new GameModeRatingsDto { MoveDuels = 1000, NoMoveDuels = 1000, NmpzDuels = 1000 }
        });
    }

    public Task<ReadClubActivitiesResponseDto> ReadClubActivitiesAsync(Guid clubId, ReadClubActivitiesQueryParams @params, CancellationToken cancellationToken = default)
    {
        var response = new ReadClubActivitiesResponseDto
        {
            Items = [],
            PaginationToken = null
        };

        if (!dataStore.ClubActivities.TryGetValue(clubId, out var activities))
            return Task.FromResult(response);

        var sorted = activities.OrderByDescending(a => a.RecordedAt).ToList();

        var startIndex = 0;
        if (@params.PaginationToken is not null && int.TryParse(@params.PaginationToken, out var parsedIndex))
            startIndex = parsedIndex;

        var page = sorted.Skip(startIndex).Take(@params.Limit).ToList();
        var nextIndex = startIndex + page.Count;

        response.Items = page;
        response.PaginationToken = nextIndex < sorted.Count ? nextIndex.ToString() : null;

        return Task.FromResult(response);
    }
}
