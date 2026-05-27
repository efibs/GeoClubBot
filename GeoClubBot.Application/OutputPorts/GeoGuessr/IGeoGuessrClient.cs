namespace UseCases.OutputPorts.GeoGuessr;

public interface IGeoGuessrClient
{
    Task<List<ClubMemberDto>> ReadClubMembersAsync(Guid clubId, CancellationToken cancellationToken = default);

    Task<ClubDto> ReadClubAsync(Guid clubId, CancellationToken cancellationToken = default);

    Task<UserDto> ReadUserAsync(string userId, CancellationToken cancellationToken = default);

    Task<PostChallengeResponseDto> CreateChallengeAsync(PostChallengeRequestDto request, CancellationToken cancellationToken = default);

    Task<ChallengeResultHighscoresDto> ReadHighscoresAsync(string challengeId, ReadHighscoresQueryParams @params, CancellationToken cancellationToken = default);

    Task<ReadClubActivitiesResponseDto> ReadClubActivitiesAsync(Guid clubId, ReadClubActivitiesQueryParams @params, CancellationToken cancellationToken = default);

    Task<DailyMissionsResponseDto> ReadDailyMissionsAsync(CancellationToken cancellationToken = default);
}
