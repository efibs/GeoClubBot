using Entities;

namespace UseCases.UseCases.ClubMemberActivity.ClubSwaps;

/// <summary>A second-club member with their average rule XP and club.</summary>
public sealed record SwapCandidate(string UserId, string Nickname, double AverageXp, string ClubName);

/// <summary>A second-club member to take the spot of a main-club member.</summary>
/// <param name="Outgoing">The main-club member leaving; null when the spot is free.</param>
public sealed record SwapSuggestion(SwapCandidate Incoming, ClubMemberAverageXp? Outgoing, SwapReason Reason);

public enum SwapReason
{
    /// <summary>The main-club member ran out of strikes and is kicked anyway.</summary>
    ReplacesKickedMember,

    /// <summary>The main club has a free spot.</summary>
    FillsFreeSpot,

    /// <summary>The second-club member's average beats the main-club member's by at least the buffer.</summary>
    BetterAverage
}

public sealed record SwapPlanOptions(int BufferXp, int MaxClubSize, bool ReplaceKickedMembers, bool FillFreeSpots);

/// <summary>
/// Decides which second-club members should move into the main club, following the club rules:
/// kicked members are replaced first, free spots are filled next, and then a second-club member
/// swaps with a main-club member whenever their average is higher by at least the buffer. The best
/// second-club members always go first, and nobody is used twice.
/// </summary>
public static class ClubSwapPlanner
{
    public static IReadOnlyList<SwapSuggestion> Plan(
        IReadOnlyList<ClubMemberAverageXp> mainClubAverages,
        IReadOnlyList<SwapCandidate> candidates,
        IReadOnlyCollection<ClubMemberAverageXp> kickedMainMembers,
        int mainClubMemberCount,
        SwapPlanOptions options)
    {
        var suggestions = new List<SwapSuggestion>();

        // Best candidates first; ties go to whoever was listed first (stable sort).
        var remainingCandidates = new Queue<SwapCandidate>(
            candidates.OrderByDescending(c => c.AverageXp));

        if (options.ReplaceKickedMembers)
        {
            foreach (var kicked in kickedMainMembers)
            {
                if (!remainingCandidates.TryDequeue(out var incoming))
                {
                    break;
                }

                suggestions.Add(new SwapSuggestion(incoming, kicked, SwapReason.ReplacesKickedMember));
            }
        }

        if (options.FillFreeSpots)
        {
            var freeSpots = options.MaxClubSize - mainClubMemberCount;
            for (var i = 0; i < freeSpots && remainingCandidates.TryDequeue(out var incoming); i++)
            {
                suggestions.Add(new SwapSuggestion(incoming, null, SwapReason.FillsFreeSpot));
            }
        }

        var kickedUserIds = kickedMainMembers.Select(k => k.UserId).ToHashSet();

        // Weakest main-club members first, excluding those already on their way out.
        var weakestMainMembers = mainClubAverages
            .Where(m => !kickedUserIds.Contains(m.UserId))
            .OrderBy(m => m.AverageXp)
            .ThenByDescending(m => m.JoinedAt);

        foreach (var outgoing in weakestMainMembers)
        {
            if (!remainingCandidates.TryPeek(out var incoming)
                || incoming.AverageXp < outgoing.AverageXp + options.BufferXp)
            {
                // The best remaining candidate does not beat the weakest remaining member by the
                // buffer, so no one after them beats anyone stronger either.
                break;
            }

            remainingCandidates.Dequeue();
            suggestions.Add(new SwapSuggestion(incoming, outgoing, SwapReason.BetterAverage));
        }

        return suggestions;
    }
}
