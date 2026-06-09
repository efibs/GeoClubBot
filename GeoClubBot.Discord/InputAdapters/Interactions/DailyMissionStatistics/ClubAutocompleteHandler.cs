using Discord;
using Discord.Interactions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using UseCases.UseCases.Club;

namespace GeoClubBot.Discord.InputAdapters.Interactions.DailyMissionStatistics;

/// <summary>
/// Suggests the tracked clubs by name. The option value is the club id, so renamed
/// clubs keep working between suggestion and execution.
/// </summary>
public class ClubAutocompleteHandler : AutocompleteHandler
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

        var suggestions = clubs
            .Where(c => c.Name.Contains(input, StringComparison.OrdinalIgnoreCase))
            .Select(c => new AutocompleteResult(c.Name, c.ClubId.ToString()))
            .Take(25);

        return AutocompletionResult.FromSuccess(suggestions);
    }
}
