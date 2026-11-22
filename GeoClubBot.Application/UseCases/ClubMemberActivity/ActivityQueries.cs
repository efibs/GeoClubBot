using Entities;
using UseCases.Abstractions;
using Utilities;

namespace UseCases.UseCases.ClubMemberActivity;

public sealed record GetLastCheckTimeQuery : IQuery<DateTimeOffset?>;

/// <summary>
/// The member's activity in the current check period — since their club's last weekly check,
/// which runs just after GeoGuessr's board reset — with progress towards the club's requirements.
/// </summary>
public sealed record GetActivityThisWeekQuery(string UserId) : IQuery<ClubMemberActivitySummary>;

public sealed record GetActivityLastDaysQuery(string UserId, int DaysBack) : IQuery<ClubMemberActivitySummary>;

public sealed record ClubStatisticsQuery : IQuery<ClubStatistics?>;

public sealed record PlayerStatisticsQuery(string UserId) : IQuery<Result<PlayerStatistics>>;

/// <summary>Why <see cref="PlayerStatisticsQuery"/> has no statistics, each with a message for the member.</summary>
public static class PlayerStatisticsErrors
{
    public static readonly Error UnknownPlayer =
        Error.NotFound("player_statistics.unknown_player", "That player is not known to the bot. Please pick one of the suggested members.");

    /// <summary>The statistics are taken from the history in the player's current club; one who left has none (#162).</summary>
    public static Error NotInAClub(string nickname) =>
        Error.NotFound("player_statistics.not_in_a_club",
            $"The player '{nickname}' is not in a club at the moment: statistics are only shown for a player's current club.");
}

/// <param name="ClubId">The club; the main club when null.</param>
public sealed record GetActivityLeaderboardQuery(Guid? ClubId, int HistoryDepth)
    : IQuery<GetActivityLeaderboardResult>;

public sealed record GetActivityLeaderboardResult(List<ClubMemberAverageXp>? Leaderboard, string? ClubName);
