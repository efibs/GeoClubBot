using Discord;
using Discord.Interactions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using UseCases.UseCases.Excuses;

namespace GeoClubBot.Discord.InputAdapters.Interactions.Autocomplete;

/// <summary>
/// Suggests existing excuses so admins pick one instead of pasting a raw GUID. The label is
/// <c>"Nickname — from→to"</c>; the option value is the excuse id.
/// </summary>
public class ExcuseIdAutocompleteHandler : AutocompleteHandler
{
    internal readonly record struct ExcuseOption(Guid ExcuseId, string Nickname, DateTimeOffset From, DateTimeOffset To);

    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        var mediator = services.GetRequiredService<ISender>();
        var excuses = await mediator.Send(new ReadExcusesQuery()).ConfigureAwait(false);

        var options = excuses.Select(e =>
            new ExcuseOption(e.ExcuseId, e.ClubMember?.User?.Nickname ?? "Unknown", e.From, e.To));

        var input = autocompleteInteraction.Data.Current.Value?.ToString() ?? string.Empty;

        return AutocompletionResult.FromSuccess(BuildSuggestions(options, input));
    }

    internal static IEnumerable<AutocompleteResult> BuildSuggestions(IEnumerable<ExcuseOption> excuses, string input) =>
        excuses
            .OrderBy(e => e.Nickname, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.From)
            .Select(e => new AutocompleteResult(
                $"{e.Nickname} — {e.From:yyyy-MM-dd}→{e.To:yyyy-MM-dd}",
                e.ExcuseId.ToString()))
            .Where(r => r.Name.Contains(input, StringComparison.OrdinalIgnoreCase))
            .Take(25);
}
