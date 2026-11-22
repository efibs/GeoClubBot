using Discord;
using Discord.Interactions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using UseCases.UseCases.Club;

namespace GeoClubBot.Discord.InputAdapters.Interactions.Autocomplete;

/// <summary>
/// The clubs of <see cref="ClubAutocompleteHandler"/>, led by "All our clubs" (value
/// <see cref="AllClubsValue"/>), for <c>/random</c>.
/// </summary>
public class DrawClubAutocompleteHandler : AutocompleteHandler
{
    public const string AllClubsValue = "all";

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

    internal static IEnumerable<AutocompleteResult> BuildSuggestions(IReadOnlyList<Entities.Club> clubs, string input)
    {
        IEnumerable<AutocompleteResult> all = "All our clubs".Contains(input, StringComparison.OrdinalIgnoreCase)
            ? [new AutocompleteResult("All our clubs", AllClubsValue)]
            : [];

        return all.Concat(ClubAutocompleteHandler.BuildSuggestions(clubs, input)).Take(25);
    }
}
