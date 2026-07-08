using Discord;
using Discord.Interactions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using UseCases.UseCases.DailyMissionReminder;
using DomainReminder = Entities.DailyMissionReminder;

namespace GeoClubBot.Discord.InputAdapters.Interactions.Autocomplete;

/// <summary>
/// Suggests the caller's own daily mission reminders so they pick one instead of pasting a raw GUID.
/// The label is the local <c>"HH:mm (zone)"</c> (plus a message snippet when set); the option value
/// is the reminder id.
/// </summary>
public class ReminderAutocompleteHandler : AutocompleteHandler
{
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        var mediator = services.GetRequiredService<ISender>();
        var reminders = await mediator
            .Send(new ListDailyMissionRemindersQuery(context.User.Id))
            .ConfigureAwait(false);

        var input = autocompleteInteraction.Data.Current.Value?.ToString() ?? string.Empty;

        return AutocompletionResult.FromSuccess(BuildSuggestions(reminders, input));
    }

    internal static IEnumerable<AutocompleteResult> BuildSuggestions(IEnumerable<DomainReminder> reminders, string input) =>
        reminders
            .OrderBy(r => r.ReminderTimeUtc)
            .Select(r => new AutocompleteResult(BuildLabel(r), r.Id.ToString()))
            .Where(r => r.Name.Contains(input, StringComparison.OrdinalIgnoreCase))
            .Take(25);

    private static string BuildLabel(DomainReminder reminder)
    {
        var localTime = ConvertToLocal(reminder.ReminderTimeUtc, reminder.TimeZoneId);
        var tzDisplay = string.IsNullOrWhiteSpace(reminder.TimeZoneId) ? "UTC" : reminder.TimeZoneId;
        var label = $"{localTime:HH\\:mm} ({tzDisplay})";

        if (!string.IsNullOrWhiteSpace(reminder.CustomMessage))
        {
            var snippet = reminder.CustomMessage.Length > 40
                ? reminder.CustomMessage[..40] + "…"
                : reminder.CustomMessage;
            label += $" — {snippet}";
        }

        // Discord truncates choice names at 100 chars; keep well within it.
        return label.Length > 100 ? label[..100] : label;
    }

    private static TimeOnly ConvertToLocal(TimeOnly utcTime, string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return utcTime;
        }

        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var utcDateTime = today.ToDateTime(utcTime, DateTimeKind.Utc);
            var localDateTime = TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, tz);
            return TimeOnly.FromDateTime(localDateTime);
        }
        catch
        {
            return utcTime;
        }
    }
}
