namespace UseCases.OutputPorts.GeoGuessr;

public interface IGeoGuessrClient
{
    Task<List<ClubMemberDto>> ReadClubMembersAsync(Guid clubId, CancellationToken cancellationToken = default);

    Task<ClubDto> ReadClubAsync(Guid clubId, CancellationToken cancellationToken = default);

    Task<UserDto> ReadUserAsync(string userId, CancellationToken cancellationToken = default);

    Task<PostChallengeResponseDto> CreateChallengeAsync(PostChallengeRequestDto request, CancellationToken cancellationToken = default);

    Task<ChallengeResultHighscoresDto> ReadHighscoresAsync(string challengeId, ReadHighscoresQueryParams @params, CancellationToken cancellationToken = default);

    Task<ReadClubActivitiesResponseDto> ReadClubActivitiesAsync(Guid clubId, ReadClubActivitiesQueryParams @params, CancellationToken cancellationToken = default);

    /// <summary>The running mission board of the club the client's account belongs to.</summary>
    Task<ClubMissionBoardSnapshotDto> ReadClubMissionBoardAsync(CancellationToken cancellationToken = default);

    /// <summary>The previous week's board of the client's club; null when there was none.</summary>
    Task<ClubMissionBoardSnapshotDto?> ReadPreviousClubMissionBoardAsync(CancellationToken cancellationToken = default);

    Task<RankedProgressResponseDto> ReadRankedProgressOfUserAsync(string userId, CancellationToken cancellationToken = default);

    Task<RankedPeakRatingResponseDto> ReadRankedPeakRatingOfUserAsync(string userId, CancellationToken cancellationToken = default);
}
