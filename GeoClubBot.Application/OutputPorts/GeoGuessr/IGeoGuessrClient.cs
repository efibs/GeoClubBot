namespace UseCases.OutputPorts.GeoGuessr;

public interface IGeoGuessrClient
{
    Task<List<ClubMemberDto>> ReadClubMembersAsync(Guid clubId, CancellationToken cancellationToken = default);

    Task<ClubDto> ReadClubAsync(Guid clubId, CancellationToken cancellationToken = default);

    Task<UserDto> ReadUserAsync(string userId, CancellationToken cancellationToken = default);

    Task<PostChallengeResponseDto> CreateChallengeAsync(PostChallengeRequestDto request, CancellationToken cancellationToken = default);

    Task<ChallengeResultHighscoresDto> ReadHighscoresAsync(string challengeId, ReadHighscoresQueryParams @params, CancellationToken cancellationToken = default);

    /// <summary>
    /// One game by its token (<c>GET /v3/games/{token}</c>): a single game, or one player's game of a
    /// challenge. Any account can read any game whose token it has (read 2026-11-21). A game still being
    /// played lists the rounds played so far, the current one included. Null when GeoGuessr knows no game
    /// by that token (404), which is also its answer for a challenge's token.
    /// </summary>
    Task<ChallengeResultGameDto?> ReadGameAsync(string gameToken, CancellationToken cancellationToken = default);

    Task<ReadClubActivitiesResponseDto> ReadClubActivitiesAsync(Guid clubId, ReadClubActivitiesQueryParams @params, CancellationToken cancellationToken = default);

    /// <summary>
    /// The club's pending applications. Needs a token with the ManageApplications permission in the
    /// club; GeoGuessr answers 404 to any other account.
    /// </summary>
    Task<ReadClubApplicationsResponseDto> ReadClubApplicationsAsync(Guid clubId, string? paginationToken = null, CancellationToken cancellationToken = default);

    /// <summary>The running mission board of the club the client's account belongs to.</summary>
    Task<ClubMissionBoardSnapshotDto> ReadClubMissionBoardAsync(CancellationToken cancellationToken = default);

    /// <summary>The previous week's board of the client's club; null when there was none.</summary>
    Task<ClubMissionBoardSnapshotDto?> ReadPreviousClubMissionBoardAsync(CancellationToken cancellationToken = default);

    Task<RankedProgressResponseDto> ReadRankedProgressOfUserAsync(string userId, CancellationToken cancellationToken = default);

    Task<RankedPeakRatingResponseDto> ReadRankedPeakRatingOfUserAsync(string userId, CancellationToken cancellationToken = default);
}
