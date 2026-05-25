namespace UseCases.OutputPorts.GeoGuessr;

public interface IGeoGuessrClient
{
    Task<List<ClubMemberDto>> ReadClubMembersAsync(Guid clubId);

    Task<ClubDto> ReadClubAsync(Guid clubId);

    Task<UserDto> ReadUserAsync(string userId);

    Task<PostChallengeResponseDto> CreateChallengeAsync(PostChallengeRequestDto request);

    Task<ChallengeResultHighscoresDto> ReadHighscoresAsync(string challengeId, ReadHighscoresQueryParams @params);

    Task<ReadClubActivitiesResponseDto> ReadClubActivitiesAsync(Guid clubId, ReadClubActivitiesQueryParams @params);

    Task<DailyMissionsResponseDto> ReadDailyMissionsAsync();
}
