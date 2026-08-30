using Configuration;
using Entities;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.GeoGuessr;

namespace GeoClubBot.Tests.TestBuilders;

/// <summary>
/// Builds club activity feed entries and the classifier that reads them. Entries carry GeoGuessr's
/// activity type, because the daily mission and the daily challenge / duel win are both worth
/// 20 XP and only the type separates them.
/// </summary>
public static class ClubActivities
{
    public static ClubActivityKindClassifier Classifier(ClubXpConfiguration? config = null) =>
        new(Options.Create(config ?? new ClubXpConfiguration()));

    /// <summary>A completed daily mission (activity type 1).</summary>
    public static ReadClubActivitiesItemDto Mission(string userId, DateTimeOffset? recordedAt = null) =>
        Of(userId, ClubXpActivityKind.DailyMission, 20, recordedAt);

    /// <summary>A daily challenge played or a duel won (activity type 4).</summary>
    public static ReadClubActivitiesItemDto Challenge(string userId, DateTimeOffset? recordedAt = null) =>
        Of(userId, ClubXpActivityKind.DailyChallengeOrDuel, 20, recordedAt);

    /// <summary>A completed weekly mission (activity type 2).</summary>
    public static ReadClubActivitiesItemDto Weekly(string userId, DateTimeOffset? recordedAt = null) =>
        Of(userId, ClubXpActivityKind.WeeklyMission, 1000, recordedAt);

    /// <summary>A club challenge being played (activity type 3) - carries no XP.</summary>
    public static ReadClubActivitiesItemDto ClubChallenge(string userId, DateTimeOffset? recordedAt = null) =>
        Of(userId, ClubXpActivityKind.ClubChallengePlayed, 0, recordedAt);

    public static ReadClubActivitiesItemDto Of(
        string userId,
        ClubXpActivityKind kind,
        int xpReward,
        DateTimeOffset? recordedAt = null) => new()
        {
            UserId = userId,
            Type = (int)kind,
            XpReward = xpReward,
            RecordedAt = recordedAt ?? DateTimeOffset.UtcNow
        };

    /// <summary>
    /// An entry without a type, as a stand-in or an old capture would produce it. The classifier
    /// falls back to the XP amount for these.
    /// </summary>
    public static ReadClubActivitiesItemDto Untyped(string userId, int xpReward, DateTimeOffset? recordedAt = null) => new()
    {
        UserId = userId,
        XpReward = xpReward,
        RecordedAt = recordedAt ?? DateTimeOffset.UtcNow
    };
}
