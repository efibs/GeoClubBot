using System.Collections.Concurrent;
using UseCases.OutputPorts.GeoGuessr;

namespace GeoClubBot.MockGeoGuessr.DataStore;

public class MockGeoGuessrDataStore
{
    /// <summary>
    /// Clubs indexed by ClubId.
    /// </summary>
    public ConcurrentDictionary<Guid, ClubDto> Clubs { get; } = new();

    /// <summary>
    /// Club members indexed by (ClubId, UserId).
    /// Outer key: ClubId, Inner key: UserId.
    /// </summary>
    public ConcurrentDictionary<Guid, ConcurrentDictionary<string, ClubMemberDto>> ClubMembers { get; } = new();

    /// <summary>
    /// Users indexed by UserId.
    /// </summary>
    public ConcurrentDictionary<string, UserDto> Users { get; } = new();

    /// <summary>
    /// Challenge requests indexed by challenge token.
    /// </summary>
    public ConcurrentDictionary<string, PostChallengeRequestDto> Challenges { get; } = new();

    /// <summary>
    /// Challenge highscores indexed by challenge token.
    /// </summary>
    public ConcurrentDictionary<string, ConcurrentBag<ChallengeResultItemDto>> ChallengeHighscores { get; } = new();

    /// <summary>
    /// Club activities indexed by ClubId.
    /// </summary>
    public ConcurrentDictionary<Guid, ConcurrentBag<ReadClubActivitiesItemDto>> ClubActivities { get; } = new();

    /// <summary>
    /// Weekly mission boards indexed by ClubId, created on first use.
    /// </summary>
    public ConcurrentDictionary<Guid, MockClubMissionBoard> MissionBoards { get; } = new();

    /// <summary>The club's mission board, starting a week at the most recent Wednesday 11:00 UTC if it has none.</summary>
    public MockClubMissionBoard GetMissionBoard(Guid clubId) =>
        MissionBoards.GetOrAdd(clubId, _ => new MockClubMissionBoard(CurrentPeriodStart(DateTimeOffset.UtcNow)));

    /// <summary>GeoGuessr's board week starts on Wednesdays at 11:00 UTC (12:00 UK summer time).</summary>
    public static DateTimeOffset CurrentPeriodStart(DateTimeOffset now)
    {
        var start = new DateTimeOffset(now.UtcDateTime.Date.AddHours(11), TimeSpan.Zero);
        while (start.DayOfWeek != DayOfWeek.Wednesday || start > now)
        {
            start = start.AddDays(-1);
        }

        return start;
    }

    /// <summary>
    /// Ranked system progress indexed by UserId.
    /// </summary>
    public ConcurrentDictionary<string, RankedProgressResponseDto> RankedProgress { get; } = new();

    /// <summary>
    /// Ranked system peak ratings indexed by UserId.
    /// </summary>
    public ConcurrentDictionary<string, RankedPeakRatingResponseDto> RankedPeakRatings { get; } = new();

    private const string TokenCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>
    /// A 16-character token like GeoGuessr's own: the bot stores challenge ids in 16-character columns,
    /// so a longer mock token cannot be stored at all. Random rather than counted, so a token issued
    /// after a restart never repeats one the database still holds from an earlier run.
    /// </summary>
    public string GenerateChallengeToken() =>
        "MOCK" + new string(Random.Shared.GetItems(TokenCharacters.AsSpan(), 12));

    /// <summary>
    /// Appends an activity. <paramref name="type"/> is GeoGuessr's activity type - 3 club challenge,
    /// 4 daily challenge / duel, 5 board mission, 6 board-clear bonus (1 and 2, the old daily and
    /// weekly missions, ended in 2026) - which is what the bot classifies on, since several sources
    /// are worth the same 20 XP.
    /// </summary>
    public void AddActivity(Guid clubId, string userId, int xpReward, int type)
    {
        var activities = ClubActivities.GetOrAdd(clubId, _ => []);
        activities.Add(new ReadClubActivitiesItemDto
        {
            UserId = userId,
            Type = type,
            XpReward = xpReward,
            RecordedAt = DateTimeOffset.UtcNow
        });
    }
}
