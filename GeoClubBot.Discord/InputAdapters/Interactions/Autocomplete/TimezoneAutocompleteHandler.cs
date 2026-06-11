using Discord;
using Discord.Interactions;

namespace GeoClubBot.Discord.InputAdapters.Interactions.Autocomplete;

/// <summary>
/// Suggests IANA timezone IDs from the system timezone database. Purely in-memory and static,
/// so it is the fastest of the autocomplete handlers. The option value is the IANA ID.
/// </summary>
public class TimezoneAutocompleteHandler : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        var input = autocompleteInteraction.Data.Current.Value?.ToString() ?? string.Empty;

        var suggestions = BuildSuggestions(TimeZoneInfo.GetSystemTimeZones(), input);

        return Task.FromResult(AutocompletionResult.FromSuccess(suggestions));
    }

    internal static IEnumerable<AutocompleteResult> BuildSuggestions(IReadOnlyList<TimeZoneInfo> timezones, string input) =>
        timezones
            .Where(tz => tz.Id.Contains(input, StringComparison.OrdinalIgnoreCase))
            .Select(tz => new AutocompleteResult(tz.Id, tz.Id))
            .Take(25);
}
