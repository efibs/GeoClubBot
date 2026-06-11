using Discord;
using Discord.Interactions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using UseCases.UseCases.Users;

namespace GeoClubBot.Discord.InputAdapters.Interactions.Autocomplete;

/// <summary>
/// Suggests the nicknames of all linked GeoGuessr users (not just club members). Used by
/// commands that make sense for any linked user, e.g. <c>/user-info discord-user</c>. The
/// option value is the nickname.
/// </summary>
public class LinkedUserNicknameAutocompleteHandler : AutocompleteHandler
{
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        var mediator = services.GetRequiredService<ISender>();
        var nicknames = await mediator.Send(new GetLinkedUserNicknamesQuery()).ConfigureAwait(false);

        var input = autocompleteInteraction.Data.Current.Value?.ToString() ?? string.Empty;

        return AutocompletionResult.FromSuccess(NicknameSuggestions.Build(nicknames, input));
    }
}
