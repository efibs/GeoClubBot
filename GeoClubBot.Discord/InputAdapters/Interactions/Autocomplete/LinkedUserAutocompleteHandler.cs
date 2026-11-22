using Discord;
using Discord.Interactions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using UseCases.UseCases.Users;

namespace GeoClubBot.Discord.InputAdapters.Interactions.Autocomplete;

/// <summary>
/// Suggests all linked GeoGuessr users (not just club members) by nickname. Used by commands that
/// make sense for any linked user, e.g. <c>/user-info discord-user</c>. The option value is the
/// GeoGuessr user id.
/// </summary>
public class LinkedUserAutocompleteHandler : AutocompleteHandler
{
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        var mediator = services.GetRequiredService<ISender>();
        var users = await mediator.Send(new GetLinkedUserNamesQuery()).ConfigureAwait(false);

        var input = autocompleteInteraction.Data.Current.Value?.ToString() ?? string.Empty;

        return AutocompletionResult.FromSuccess(UserSuggestions.Build(users, input));
    }
}
