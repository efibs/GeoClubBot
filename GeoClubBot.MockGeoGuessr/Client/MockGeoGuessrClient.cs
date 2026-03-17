using System.Net;
using GeoClubBot.MockGeoGuessr.DataStore;
using UseCases.OutputPorts.GeoGuessr;

namespace GeoClubBot.MockGeoGuessr.Client;

public class MockGeoGuessrClient(MockGeoGuessrDataStore dataStore) : IGeoGuessrClient
{
    public Task<List<ClubMemberDto>> ReadClubMembersAsync(Guid clubId)
    {
        if (dataStore.ClubMembers.TryGetValue(clubId, out var members))
            return Task.FromResult(members.Values.ToList());

        return Task.FromResult(new List<ClubMemberDto>());
    }

    public Task<ClubDto> ReadClubAsync(Guid clubId)
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

    public Task<UserDto> ReadUserAsync(string userId)
    {
        if (!dataStore.Users.TryGetValue(userId, out var user))
            throw new HttpRequestException($"User {userId} not found", null, HttpStatusCode.NotFound);

        return Task.FromResult(user);
    }

    public Task<PostChallengeResponseDto> CreateChallengeAsync(PostChallengeRequestDto request)
    {
        var token = dataStore.GenerateChallengeToken();
        dataStore.Challenges[token] = request;
        dataStore.ChallengeHighscores[token] = [];
        dataStore.NotifyDataChanged();
        return Task.FromResult(new PostChallengeResponseDto { Token = token });
    }

    public Task<ChallengeResultHighscoresDto> ReadHighscoresAsync(string challengeId, ReadHighscoresQueryParams @params)
    {
        var items = new List<ChallengeResultItemDto>();

        if (dataStore.ChallengeHighscores.TryGetValue(challengeId, out var scores))
            items = scores.Take(@params.Limit).ToList();

        return Task.FromResult(new ChallengeResultHighscoresDto { Items = items });
    }

    public Task<ReadClubActivitiesResponseDto> ReadClubActivitiesAsync(Guid clubId, ReadClubActivitiesQueryParams @params)
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
