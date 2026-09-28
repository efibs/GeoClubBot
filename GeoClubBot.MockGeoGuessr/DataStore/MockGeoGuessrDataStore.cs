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
    /// Current daily missions served by the mock missions endpoint.
    /// </summary>
    public List<DailyMissionDto> DailyMissions { get; } = new();

    /// <summary>
    /// The next mission date returned by the mock missions endpoint.
    /// </summary>
    public DateTimeOffset NextMissionDate { get; set; } = DateTimeOffset.UtcNow.Date.AddDays(1);

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
    /// Appends an activity. <paramref name="type"/> is GeoGuessr's activity type - 1 daily mission,
    /// 2 weekly mission, 4 daily challenge / duel - which is what the bot classifies on now
    /// that the daily mission and the daily challenge are both worth 20 XP.
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
