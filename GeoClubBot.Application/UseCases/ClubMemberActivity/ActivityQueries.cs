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

public sealed record PlayerStatisticsQuery(string Nickname) : IQuery<Result<PlayerStatistics>>;

/// <summary>Why <see cref="PlayerStatisticsQuery"/> has no statistics, each with a message for the member.</summary>
public static class PlayerStatisticsErrors
{
    public static Error UnknownPlayer(string nickname) =>
        Error.NotFound("player_statistics.unknown_player", $"No player with the GeoGuessr nickname '{nickname}' is known to the bot.");

    /// <summary>The statistics are taken from the history in the player's current club; one who left has none (#162).</summary>
    public static Error NotInAClub(string nickname) =>
        Error.NotFound("player_statistics.not_in_a_club",
            $"The player '{nickname}' is not in a club at the moment: statistics are only shown for a player's current club.");
}

public sealed record GetActivityLeaderboardQuery(string? ClubName, int HistoryDepth)
    : IQuery<GetActivityLeaderboardResult>;

public sealed record GetActivityLeaderboardResult(List<ClubMemberAverageXp>? Leaderboard, string? ClubName);
