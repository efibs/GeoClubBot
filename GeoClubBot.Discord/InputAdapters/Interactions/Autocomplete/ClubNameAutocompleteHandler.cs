using Discord;
using Discord.Interactions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using UseCases.UseCases.Club;

namespace GeoClubBot.Discord.InputAdapters.Interactions.Autocomplete;

/// <summary>
/// Suggests the tracked clubs by name. Unlike <c>ClubAutocompleteHandler</c> (whose value is
/// the club id), the option value here is the club <b>name</b>, because the consuming commands
/// resolve the club by name (<c>ReadClubByNameAsync</c>).
/// </summary>
public class ClubNameAutocompleteHandler : AutocompleteHandler
{
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        var mediator = services.GetRequiredService<ISender>();
        var clubs = await mediator.Send(new GetAllClubsQuery()).ConfigureAwait(false);

        var input = autocompleteInteraction.Data.Current.Value?.ToString() ?? string.Empty;

        return AutocompletionResult.FromSuccess(BuildSuggestions(clubs, input));
    }

    internal static IEnumerable<AutocompleteResult> BuildSuggestions(IReadOnlyList<Entities.Club> clubs, string input) =>
        clubs
            .Where(c => c.Name.Contains(input, StringComparison.OrdinalIgnoreCase))
            .Select(c => new AutocompleteResult(c.Name, c.Name))
            .Take(25);
}
