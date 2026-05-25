using Refit;
using UseCases.OutputPorts.GeoGuessr;

namespace Infrastructure.OutputAdapters.GeoGuessr;

public class RefitGeoGuessrClient(IGeoGuessrApi api) : IGeoGuessrClient
{
    public static RefitGeoGuessrClient FromHttpClient(HttpClient httpClient) =>
        new(RestService.For<IGeoGuessrApi>(httpClient));

    public Task<List<ClubMemberDto>> ReadClubMembersAsync(Guid clubId) =>
        api.ReadClubMembersAsync(clubId);

    public Task<ClubDto> ReadClubAsync(Guid clubId) =>
        api.ReadClubAsync(clubId);

    public Task<UserDto> ReadUserAsync(string userId) =>
        api.ReadUserAsync(userId);

    public Task<PostChallengeResponseDto> CreateChallengeAsync(PostChallengeRequestDto request) =>
        api.CreateChallengeAsync(request);

    public Task<ChallengeResultHighscoresDto> ReadHighscoresAsync(string challengeId, ReadHighscoresQueryParams @params) =>
        api.ReadHighscoresAsync(challengeId, @params);

    public Task<ReadClubActivitiesResponseDto> ReadClubActivitiesAsync(Guid clubId, ReadClubActivitiesQueryParams @params) =>
        api.ReadClubActivitiesAsync(clubId, @params);

    public Task<DailyMissionsResponseDto> ReadDailyMissionsAsync() =>
        api.ReadDailyMissionsAsync();
}
