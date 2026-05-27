using Refit;
using UseCases.OutputPorts.GeoGuessr;

namespace Infrastructure.OutputAdapters.GeoGuessr;

public class RefitGeoGuessrClient(IGeoGuessrApi api) : IGeoGuessrClient
{
    public static RefitGeoGuessrClient FromHttpClient(HttpClient httpClient) =>
        new(RestService.For<IGeoGuessrApi>(httpClient));

    public Task<List<ClubMemberDto>> ReadClubMembersAsync(Guid clubId, CancellationToken cancellationToken = default) =>
        api.ReadClubMembersAsync(clubId, cancellationToken);

    public Task<ClubDto> ReadClubAsync(Guid clubId, CancellationToken cancellationToken = default) =>
        api.ReadClubAsync(clubId, cancellationToken);

    public Task<UserDto> ReadUserAsync(string userId, CancellationToken cancellationToken = default) =>
        api.ReadUserAsync(userId, cancellationToken);

    public Task<PostChallengeResponseDto> CreateChallengeAsync(PostChallengeRequestDto request, CancellationToken cancellationToken = default) =>
        api.CreateChallengeAsync(request, cancellationToken);

    public Task<ChallengeResultHighscoresDto> ReadHighscoresAsync(string challengeId, ReadHighscoresQueryParams @params, CancellationToken cancellationToken = default) =>
        api.ReadHighscoresAsync(challengeId, @params, cancellationToken);

    public Task<ReadClubActivitiesResponseDto> ReadClubActivitiesAsync(Guid clubId, ReadClubActivitiesQueryParams @params, CancellationToken cancellationToken = default) =>
        api.ReadClubActivitiesAsync(clubId, @params, cancellationToken);

    public Task<DailyMissionsResponseDto> ReadDailyMissionsAsync(CancellationToken cancellationToken = default) =>
        api.ReadDailyMissionsAsync(cancellationToken);
}
