using Discord;

namespace GeoClubBot.Discord.InputAdapters.Interactions.Autocomplete;

/// <summary>
/// Shared filter/map logic for the nickname autocomplete handlers. Kept separate from the
/// Discord-context plumbing so it can be unit-tested directly.
/// </summary>
internal static class NicknameSuggestions
{
    public static IEnumerable<AutocompleteResult> Build(IReadOnlyList<string> nicknames, string input) =>
        nicknames
            .Where(n => n.Contains(input, StringComparison.OrdinalIgnoreCase))
            .Select(n => new AutocompleteResult(n, n))
            .Take(25);
}
