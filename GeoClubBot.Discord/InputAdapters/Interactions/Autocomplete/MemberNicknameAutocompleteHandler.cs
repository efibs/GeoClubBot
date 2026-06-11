using Discord;
using Discord.Interactions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using UseCases.UseCases.ClubMembers;

namespace GeoClubBot.Discord.InputAdapters.Interactions.Autocomplete;

/// <summary>
/// Suggests the nicknames of tracked club members. Used by commands that only operate on
/// club members (activity, strikes, excuses, statistics). The option value is the nickname.
/// </summary>
public class MemberNicknameAutocompleteHandler : AutocompleteHandler
{
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        var mediator = services.GetRequiredService<ISender>();
        var nicknames = await mediator.Send(new GetTrackedMemberNicknamesQuery()).ConfigureAwait(false);

        var input = autocompleteInteraction.Data.Current.Value?.ToString() ?? string.Empty;

        return AutocompletionResult.FromSuccess(NicknameSuggestions.Build(nicknames, input));
    }
}
