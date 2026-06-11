using Discord;
using Discord.Interactions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using UseCases.UseCases.Strikes;

namespace GeoClubBot.Discord.InputAdapters.Interactions.Autocomplete;

/// <summary>
/// Suggests existing strikes so admins pick one instead of pasting a raw GUID. The label is
/// <c>"Nickname — date"</c>; the option value is the strike id.
/// </summary>
public class StrikeIdAutocompleteHandler : AutocompleteHandler
{
    internal readonly record struct StrikeOption(Guid StrikeId, string Nickname, DateTimeOffset Timestamp);

    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        var mediator = services.GetRequiredService<ISender>();
        var strikes = await mediator.Send(new ReadAllStrikesQuery()).ConfigureAwait(false);

        var options = strikes.Select(s =>
            new StrikeOption(s.StrikeId, s.ClubMember?.User?.Nickname ?? "Unknown", s.Timestamp));

        var input = autocompleteInteraction.Data.Current.Value?.ToString() ?? string.Empty;

        return AutocompletionResult.FromSuccess(BuildSuggestions(options, input));
    }

    internal static IEnumerable<AutocompleteResult> BuildSuggestions(IEnumerable<StrikeOption> strikes, string input) =>
        strikes
            .OrderBy(s => s.Nickname, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Timestamp)
            .Select(s => new AutocompleteResult(
                $"{s.Nickname} — {s.Timestamp:yyyy-MM-dd}",
                s.StrikeId.ToString()))
            .Where(r => r.Name.Contains(input, StringComparison.OrdinalIgnoreCase))
            .Take(25);
}
