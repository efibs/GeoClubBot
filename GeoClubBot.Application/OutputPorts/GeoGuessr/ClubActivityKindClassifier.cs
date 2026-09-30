using Configuration;
using Entities;
using Microsoft.Extensions.Options;

namespace UseCases.OutputPorts.GeoGuessr;

/// <summary>
/// Turns a club activity feed entry into the reason it awarded XP.
///
/// The single place that knows how GeoGuessr labels activities. Everything that used to ask
/// "is this entry worth exactly 20 XP?" asks this instead, because that question has several
/// answers: the daily challenge / duel and a board mission are both worth 20.
///
/// Classification prefers the feed's own <c>type</c> field. When it is absent — a stand-in that
/// doesn't set it, or a fixture written before the field was known — it falls back to the
/// configured <see cref="ClubXpConfiguration.UntypedXpFallback"/> map, and otherwise to unknown.
/// </summary>
public sealed class ClubActivityKindClassifier
{
    private readonly Dictionary<int, ClubXpActivityKind> _untypedFallback;

    public ClubActivityKindClassifier(IOptions<ClubXpConfiguration> config)
    {
        _untypedFallback = config.Value.UntypedXpFallback.ToDictionary(
            e => e.Key,
            e => Enum.TryParse<ClubXpActivityKind>(e.Value, ignoreCase: true, out var kind)
                ? kind
                : throw new InvalidOperationException(
                    $"{ClubXpConfiguration.SectionName}:{nameof(ClubXpConfiguration.UntypedXpFallback)}:{e.Key} " +
                    $"names the unknown activity kind '{e.Value}'. Valid kinds: {string.Join(", ", Enum.GetNames<ClubXpActivityKind>())}."));
    }

    public ClubXpActivityKind Classify(ReadClubActivitiesItemDto activity)
    {
        if (activity.Type is { } type)
        {
            return Enum.IsDefined(typeof(ClubXpActivityKind), type)
                ? (ClubXpActivityKind)type
                : ClubXpActivityKind.Unknown;
        }

        return _untypedFallback.GetValueOrDefault(activity.XpReward, ClubXpActivityKind.Unknown);
    }

    /// <summary>The daily challenge or a duel was played — the entry that extends the daily streak.</summary>
    public bool IsDailyChallenge(ReadClubActivitiesItemDto activity) =>
        Classify(activity) == ClubXpActivityKind.DailyChallengeOrDuel;

    /// <summary>A mission on the club mission board was completed, credited to its claimer.</summary>
    public bool IsBoardMission(ReadClubActivitiesItemDto activity) =>
        Classify(activity) == ClubXpActivityKind.BoardMission;

    /// <summary>A mission board was cleared, credited to whoever completed its last mission.</summary>
    public bool IsBoardClearBonus(ReadClubActivitiesItemDto activity) =>
        Classify(activity) == ClubXpActivityKind.BoardClearBonus;
}
