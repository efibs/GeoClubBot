using Discord;
using Discord.Interactions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using UseCases.UseCases.DailyMissionStatistics;

namespace GeoClubBot.Discord.InputAdapters.Interactions.DailyMissionStatistics;

/// <summary>
/// Suggests the mission kinds that have ever been logged. The option value is the
/// machine-readable <c>"Type|GameMode"</c> pair; the label is the friendly English phrase.
/// </summary>
public class MissionKindAutocompleteHandler : AutocompleteHandler
{
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        var mediator = services.GetRequiredService<ISender>();
        var kinds = await mediator.Send(new GetDailyMissionKindsQuery()).ConfigureAwait(false);

        var input = autocompleteInteraction.Data.Current.Value?.ToString() ?? string.Empty;

        var suggestions = kinds
            .Select(k => new AutocompleteResult(
                DailyMissionStatisticsFormatter.KindLabel(k.Type, k.GameMode),
                $"{k.Type}|{k.GameMode}"))
            .Where(r => r.Name.Contains(input, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Take(25);

        return AutocompletionResult.FromSuccess(suggestions);
    }
}
