using Constants;
using Discord;
using Discord.Interactions;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.UseCases.CountryChallenges;

namespace GeoClubBot.Discord.InputAdapters.Interactions.CountryChallenges;

[CommandContextType(InteractionContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.Administrator)]
[Group("country-challenges-admin", "Manage the country challenges")]
public partial class CountryChallengesAdminModule(
    ISender mediator,
    ILogger<CountryChallengesAdminModule> logger) : ClubBotInteractionModule(mediator, logger)
{
    [SlashCommand("preview", "Check the country challenge file and show what would be posted on a day, without posting")]
    public Task PreviewAsync(
        [Summary(description: "A date (yyyy-MM-dd) or a weekday such as 'sunday'; default today")]
        string? day = null) =>
        ExecuteAsync(
            async ct =>
            {
                DateOnly? date = null;
                DayOfWeek? weekday = null;
                if (day is not null && !CountryChallengesFormatter.TryParseDay(day, out date, out weekday))
                {
                    await FollowupAsync($"'{day}' is neither a date (yyyy-MM-dd) nor a weekday.", ephemeral: true)
                        .ConfigureAwait(false);
                    return;
                }

                var result = await Mediator.Send(new PreviewCountryChallengesQuery(date, weekday), ct).ConfigureAwait(false);
                if (result.IsFailure)
                {
                    await SendAsync([FriendlyMessageFor(result.Error)]).ConfigureAwait(false);
                    return;
                }

                await SendAsync(CountryChallengesFormatter.Preview(result.Value)).ConfigureAwait(false);
            },
            ephemeral: true,
            failureMessage: "Failed to preview the country challenges. Please check the logs.");

    [SlashCommand("post-now", "Run the country challenges for today now; anything already posted today is skipped")]
    public Task PostNowAsync() =>
        ExecuteAsync(
            async ct =>
            {
                var result = await Mediator.Send(new RunCountryChallengesCommand(), ct).ConfigureAwait(false);
                if (result.IsFailure)
                {
                    await SendAsync([FriendlyMessageFor(result.Error)]).ConfigureAwait(false);
                    return;
                }

                await SendAsync(CountryChallengesFormatter.RunReport(result.Value)).ConfigureAwait(false);
            },
            ephemeral: true,
            failureMessage: "Running the country challenges failed. Please check the logs.");

    [SlashCommand("results-now", "Evaluate every country challenge still waiting for its results now, even before it is due")]
    public Task ResultsNowAsync() =>
        ExecuteAsync(
            async ct =>
            {
                var result = await Mediator.Send(new EvaluateCountryChallengesNowCommand(), ct).ConfigureAwait(false);
                if (result.IsFailure)
                {
                    await SendAsync([FriendlyMessageFor(result.Error)]).ConfigureAwait(false);
                    return;
                }

                await SendAsync(CountryChallengesFormatter.EvaluationReport(result.Value)).ConfigureAwait(false);
            },
            ephemeral: true,
            failureMessage: "Evaluating the country challenges failed. Please check the logs.");

    /// <summary>
    /// Opens the import form. Not routed through <see cref="ClubBotInteractionModule.ExecuteAsync"/>, which
    /// defers first — a modal has to be the interaction's immediate response.
    /// </summary>
    [SlashCommand("import-standings", "Import the standings kept by hand before the bot tracked them")]
    public async Task ImportStandingsAsync()
    {
        try
        {
            await Context.Interaction
                .RespondWithModalAsync<ImportStandingsModal>(ComponentIds.CountryChallengeImportStandingsModalId)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogImportModalOpenFailed(Logger, ex);
            await RespondAsync("Failed to open the import form.", ephemeral: true).ConfigureAwait(false);
        }
    }

    [ModalInteraction(ComponentIds.CountryChallengeImportStandingsModalId, true)]
    public Task ImportStandingsSubmittedAsync(ImportStandingsModal modal) =>
        ExecuteAsync(
            async ct =>
            {
                var result = await Mediator
                    .Send(new ImportCountryChallengeStandingsCommand(modal.Standings), ct)
                    .ConfigureAwait(false);

                if (result.IsFailure)
                {
                    await SendAsync([FriendlyMessageFor(result.Error)]).ConfigureAwait(false);
                    return;
                }

                await SendAsync(CountryChallengesFormatter.Import(result.Value)).ConfigureAwait(false);
            },
            ephemeral: true,
            failureMessage: "Importing the standings failed. Nothing was imported; please check the logs.");

    /// <summary>
    /// Ephemeral, and pinging no one: a preview shows role mentions exactly as they will look, but must not
    /// notify anybody.
    /// </summary>
    private async Task SendAsync(IEnumerable<string> messages)
    {
        foreach (var message in CountryChallengesFormatter.Split(messages))
        {
            await FollowupAsync(message, ephemeral: true, allowedMentions: AllowedMentions.None).ConfigureAwait(false);
        }
    }

    [LoggerMessage(LogLevel.Error, "Failed to open the country challenge standings import form.")]
    static partial void LogImportModalOpenFailed(ILogger logger, Exception ex);
}
