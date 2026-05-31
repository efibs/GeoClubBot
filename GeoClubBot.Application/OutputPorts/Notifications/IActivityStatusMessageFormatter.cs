using Entities;

namespace UseCases.OutputPorts.Notifications;

/// <summary>
/// Pure-function port that turns activity-check results into ready-to-send message text.
/// Implementations are platform-specific (Discord, email, Slack, ...). Chunking by message
/// length lives in the caller; the formatter is fed the slices it needs to render.
/// </summary>
public interface IActivityStatusMessageFormatter
{
    /// <summary>
    /// Renders the first message of an activity-status update: a header with the club
    /// name + XP threshold, followed by the first chunk of failed-requirement players.
    /// When <paramref name="firstChunk"/> is empty, the body shows a "None" indicator.
    /// </summary>
    string FormatStatusUpdateHeader(IReadOnlyList<ClubMemberActivityStatus> firstChunk, string clubName, int minXP);

    /// <summary>
    /// Renders an additional chunk of failed-requirement players (i.e. the continuation
    /// messages after <see cref="FormatStatusUpdateHeader"/>).
    /// </summary>
    string FormatPlayerChunk(IReadOnlyList<ClubMemberActivityStatus> players);

    /// <summary>
    /// Renders the trailing "players that had an individual target" section. Callers
    /// only invoke this when the slice is non-empty.
    /// </summary>
    string FormatIndividualTargets(IReadOnlyList<ClubMemberActivityStatus> playersWithIndividualTarget);

    /// <summary>
    /// Renders the top / bottom average-XP summary. Returns <c>null</c> if both slices
    /// are empty, signalling "nothing to send".
    /// </summary>
    string? FormatAverageXpSummary(
        IReadOnlyList<ClubMemberAverageXp> topMembers,
        IReadOnlyList<ClubMemberAverageXp> bottomMembers,
        int historyDepth);
}
