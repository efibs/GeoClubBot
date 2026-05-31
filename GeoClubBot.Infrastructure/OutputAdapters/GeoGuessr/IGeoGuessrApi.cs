using Refit;
using UseCases.OutputPorts.GeoGuessr;

namespace Infrastructure.OutputAdapters.GeoGuessr;

public interface IGeoGuessrApi
{
    [Get("/v4/clubs/{clubId}/members")]
    Task<List<ClubMemberDto>> ReadClubMembersAsync(Guid clubId, CancellationToken cancellationToken = default);

    [Get("/v4/clubs/{clubId}")]
    Task<ClubDto> ReadClubAsync(Guid clubId, CancellationToken cancellationToken = default);

    [Get("/v3/users/{userId}")]
    Task<UserDto> ReadUserAsync(string userId, CancellationToken cancellationToken = default);

    [Post("/v3/challenges")]
    Task<PostChallengeResponseDto> CreateChallengeAsync(PostChallengeRequestDto request, CancellationToken cancellationToken = default);

    [Get("/v3/results/highscores/{challengeId}")]
    Task<ChallengeResultHighscoresDto> ReadHighscoresAsync(string challengeId, [Query] ReadHighscoresQueryParams @params, CancellationToken cancellationToken = default);

    [Get("/v4/clubs/{clubId}/activities")]
    Task<ReadClubActivitiesResponseDto> ReadClubActivitiesAsync(Guid clubId, [Query] ReadClubActivitiesQueryParams @params, CancellationToken cancellationToken = default);

    [Get("/v4/missions")]
    Task<DailyMissionsResponseDto> ReadDailyMissionsAsync(CancellationToken cancellationToken = default);

    [Get("/v4/ranked-system/progress/{userId}")]
    Task<RankedProgressResponseDto> ReadRankedProgressOfUserAsync(string userId, CancellationToken cancellationToken = default);

    [Get("/v4/ranked-system/peak-rating/{userId}")]
    Task<RankedPeakRatingResponseDto> ReadRankedPeakRatingOfUserAsync(string userId, CancellationToken cancellationToken = default);
}
