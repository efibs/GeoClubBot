using Refit;

namespace UseCases.OutputPorts.GeoGuessr;

public interface IGeoGuessrClient
{
    [Get("/v4/clubs/{clubId}/members")]
    Task<List<ClubMemberDto>> ReadClubMembersAsync(Guid clubId);
    
    [Get("/v4/clubs/{clubId}")]
    Task<ClubDto> ReadClubAsync(Guid clubId);
    
    [Get("/v3/users/{userId}")]
    Task<UserDto> ReadUserAsync(string userId);

    [Post("/v3/challenges")]
    Task<PostChallengeResponseDto> CreateChallengeAsync(PostChallengeRequestDto request);
    
    [Get("/v3/results/highscores/{challengeId}")]
    Task<ChallengeResultHighscoresDto> ReadHighscoresAsync(string challengeId, [Query] ReadHighscoresQueryParams @params);
    
    [Get("/v4/clubs/{clubId}/activities")]
    Task<ReadClubActivitiesResponseDto> ReadClubActivitiesAsync(Guid clubId, [Query] ReadClubActivitiesQueryParams @params);
}