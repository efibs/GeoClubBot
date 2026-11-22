using Discord;
using UseCases.OutputPorts.Projections;

namespace GeoClubBot.Discord.InputAdapters.Interactions.Autocomplete;

/// <summary>
/// Shared filter/map logic for the user autocomplete handlers: the nickname is shown, the user id is
/// the value, so a command never has to find a member by a nickname that may be shared or changed.
/// Kept separate from the Discord-context plumbing so it can be unit-tested directly.
/// </summary>
internal static class UserSuggestions
{
    public static IEnumerable<AutocompleteResult> Build(IReadOnlyList<UserNameProjection> users, string input)
    {
        // GeoGuessr nicknames are not unique: players sharing one are told apart by their id.
        var sharedNicknames = users
            .GroupBy(u => u.Nickname, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return users
            .Where(u => u.Nickname.Contains(input, StringComparison.OrdinalIgnoreCase))
            .Select(u => new AutocompleteResult(
                sharedNicknames.Contains(u.Nickname) ? $"{u.Nickname} ({u.UserId})" : u.Nickname,
                u.UserId))
            .Take(25);
    }
}
