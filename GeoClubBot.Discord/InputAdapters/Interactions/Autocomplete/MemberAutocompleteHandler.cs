using Discord;
using Discord.Interactions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using UseCases.UseCases.ClubMembers;

namespace GeoClubBot.Discord.InputAdapters.Interactions.Autocomplete;

/// <summary>
/// Suggests tracked club members by nickname. Used by commands that only operate on club members
/// (activity, strikes, excuses, statistics). The option value is the member's GeoGuessr user id.
/// </summary>
public class MemberAutocompleteHandler : AutocompleteHandler
{
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        var mediator = services.GetRequiredService<ISender>();
        var members = await mediator.Send(new GetTrackedMemberNamesQuery()).ConfigureAwait(false);

        var input = autocompleteInteraction.Data.Current.Value?.ToString() ?? string.Empty;

        return AutocompletionResult.FromSuccess(UserSuggestions.Build(members, input));
    }
}
