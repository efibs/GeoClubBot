using System.Globalization;
using System.Text;

namespace UseCases.UseCases.ClubMemberActivity.ClubSwaps;

/// <summary>Renders the swap suggestion report posted after the weekly check.</summary>
public static class ClubSwapSuggestionMessage
{
    public static string Render(
        string mainClubName,
        IReadOnlyList<SwapSuggestion> suggestions,
        int historyDepth,
        int bufferXp)
    {
        var builder = new StringBuilder($"**======= Swap suggestions - {mainClubName} =======**\n");
        builder.Append(CultureInfo.InvariantCulture,
            $"-# Average rule XP over the last {historyDepth} week(s); a swap needs at least {bufferXp} XP more on average.\n");

        if (suggestions.Count == 0)
        {
            builder.Append("No swaps this week :)");
            return builder.ToString();
        }

        foreach (var suggestion in suggestions)
        {
            builder.AppendLine();
            var incoming = $"**{suggestion.Incoming.Nickname}** ({suggestion.Incoming.ClubName}, {Format(suggestion.Incoming.AverageXp)})";

            builder.Append(suggestion.Reason switch
            {
                SwapReason.ReplacesKickedMember =>
                    $"* 🔁 {incoming} takes the spot of **{suggestion.Outgoing!.Nickname}**, who is out of strikes",
                SwapReason.FillsFreeSpot =>
                    $"* ⬆️ {incoming} is promoted into a free spot",
                _ =>
                    $"* 🔄 {incoming} swaps with **{suggestion.Outgoing!.Nickname}** ({Format(suggestion.Outgoing.AverageXp)})"
            });
        }

        return builder.ToString();
    }

    private static string Format(double averageXp) =>
        string.Create(CultureInfo.InvariantCulture, $"{averageXp:F1} XP");
}
