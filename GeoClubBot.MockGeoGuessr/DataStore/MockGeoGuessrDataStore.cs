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

    private int _challengeCounter;

    public string GenerateChallengeToken()
    {
        var id = Interlocked.Increment(ref _challengeCounter);
        return $"mock-challenge-{id:D6}";
    }

    public event Action? OnDataChanged;

    public void NotifyDataChanged() => OnDataChanged?.Invoke();

    public void AddActivity(Guid clubId, string userId, int xpReward)
    {
        var activities = ClubActivities.GetOrAdd(clubId, _ => []);
        activities.Add(new ReadClubActivitiesItemDto
        {
            UserId = userId,
            XpReward = xpReward,
            RecordedAt = DateTimeOffset.UtcNow
        });
    }
}
