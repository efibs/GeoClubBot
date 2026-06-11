using Discord.Interactions;
using GeoClubBot.Discord.InputAdapters.Interactions.Autocomplete;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.UseCases.ClubMemberActivity;
using UseCases.UseCases.Excuses;

namespace GeoClubBot.Discord.InputAdapters.Interactions;

public partial class ActivityModule
{
    public partial class ActivityExcuseModule(
        ISender mediator,
        ILogger<ActivityExcuseModule> logger) : ClubBotInteractionModule(mediator, logger)
    {
        [SlashCommand("add", "Add an excuse for a player")]
        public async Task AddExcuseAsync(
            [Autocomplete(typeof(MemberNicknameAutocompleteHandler))] string memberNickname,
            [Summary(description: "From date in format YYYY-MM-DD")]
            DateTime from,
            [Summary(description: "To date in format YYYY-MM-DD")]
            DateTime to)
        {
            // Add one day to the to time and subtract a tick so that the day still counts.
            to = to.AddDays(1).AddTicks(-1);

            from = DateTime.SpecifyKind(from, DateTimeKind.Utc);
            to = DateTime.SpecifyKind(to, DateTimeKind.Utc);

            if (from >= to)
            {
                await RespondAsync($"Excuse could not be added: The given from date lies after the given to date.",
                    ephemeral: true).ConfigureAwait(false);
                return;
            }

            var result = await Mediator
                .Send(new AddExcuseCommand(memberNickname, from, to))
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                await RespondAsync($"Excuse could not be added for player '{memberNickname}'. Is the nickname wrong?",
                    ephemeral: true).ConfigureAwait(false);
            }
            else
            {
                await RespondAsync(
                    $"Excuse with id {result.Value} for the time range **{from:D}** to **{to:D}** was added to player " +
                    $"**{memberNickname}**.").ConfigureAwait(false);
            }
        }

        [SlashCommand("update", "Update an excuse for a player")]
        public async Task UpdateExcuseAsync(
            [Autocomplete(typeof(ExcuseIdAutocompleteHandler))] string excuseId,
            [Summary(description: "The new from date in format YYYY-MM-DD")]
            DateTime from,
            [Summary(description: "The new to date in format YYYY-MM-DD")]
            DateTime to)
        {
            if (!Guid.TryParse(excuseId, out var excuseIdGuid))
            {
                await RespondAsync($"Invalid GUID '{excuseId}'. Please enter a valid GUID.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            // Add one day to the to time and subtract a tick so that the day still counts.
            to = to.AddDays(1).AddTicks(-1);

            from = DateTime.SpecifyKind(from, DateTimeKind.Utc);
            to = DateTime.SpecifyKind(to, DateTimeKind.Utc);

            if (from >= to)
            {
                await RespondAsync($"Excuse could not be updated: The given from date lies after the given to date.",
                    ephemeral: true).ConfigureAwait(false);
                return;
            }

            var result = await Mediator
                .Send(new UpdateExcuseCommand(excuseIdGuid, from, to))
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                await RespondAsync(FriendlyMessageFor(result.Error), ephemeral: true).ConfigureAwait(false);
                return;
            }

            var updatedExcuse = result.Value;
            await RespondAsync(
                $"Excuse with id {excuseIdGuid} was updated to the time range **{updatedExcuse.From:D}** to **{updatedExcuse.To:D}**.")
                .ConfigureAwait(false);
        }

        [SlashCommand("remove", "Remove an excuse for a player given its id")]
        public async Task RemoveExcuseAsync(
            [Autocomplete(typeof(ExcuseIdAutocompleteHandler))] string excuseId)
        {
            if (!Guid.TryParse(excuseId, out var excuseIdGuid))
            {
                await RespondAsync($"Invalid GUID '{excuseId}'. Please enter a valid GUID.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            var result = await Mediator.Send(new RemoveExcuseCommand(excuseIdGuid)).ConfigureAwait(false);

            await RespondAsync(
                result.IsSuccess
                    ? $"Excuse with id {excuseId} successfully removed"
                    : $"There is no excuse with id {excuseId}",
                ephemeral: result.IsFailure).ConfigureAwait(false);
        }

        [SlashCommand("read", "Read the excuses for a player")]
        public async Task ReadExcusesAsync(
            [Autocomplete(typeof(MemberNicknameAutocompleteHandler))] string memberNickname)
        {
            var excuses = await Mediator.Send(new ReadExcusesQuery(memberNickname)).ConfigureAwait(false);

            if (excuses.Count == 0)
            {
                await RespondAsync($"The player {memberNickname} has no excuses.", ephemeral: true).ConfigureAwait(false);
            }
            else
            {
                var excusesString = string.Join("\n", excuses.Select(e => $"* {e}"));

                await RespondAsync($"The player {memberNickname} has the following excuses:\n{excusesString}",
                    ephemeral: true).ConfigureAwait(false);
            }
        }

        [SlashCommand("read-all", "Read all excuses in the system")]
        public async Task ReadExcusesAsync()
        {
            var excuses = await Mediator.Send(new ReadExcusesQuery()).ConfigureAwait(false);

            if (excuses.Count == 0)
            {
                await RespondAsync($"There are currently no excuses in the system.", ephemeral: true).ConfigureAwait(false);
            }
            else
            {
                var excusesString = string.Join("\n", excuses.Select(e => $"* {e.ToStringWithPlayerName()}"));

                await RespondAsync($"The following excuses are currently entered in the system:\n{excusesString}",
                    ephemeral: true).ConfigureAwait(false);
            }
        }

        [SlashCommand("read-relevant", "Read the relevant excuses in the system")]
        public async Task ReadRelevantExcusesAsync(int upcomingExcusesNumDays = 7)
        {
            var lastActivityCheckTime = await Mediator.Send(new GetLastCheckTimeQuery()).ConfigureAwait(false);
            var result = await Mediator
                .Send(new ReadRelevantExcusesQuery(upcomingExcusesNumDays, lastActivityCheckTime))
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                return;
            }

            var excuses = result.Value;

            if (excuses.Count == 0)
            {
                await RespondAsync($"There are currently no relevant excuses in the system.", ephemeral: true).ConfigureAwait(false);
            }
            else
            {
                var activeExcuses = excuses
                    .Where(e => !e.IsUpcoming && !e.IsPrevious).ToList();
                var upcomingExcuses = excuses
                    .Where(e => e.IsUpcoming).ToList();
                var previousExcuses = excuses
                    .Where(e => e.IsPrevious).ToList();

                var activeExcusesString = string.Join("\n", activeExcuses.Select(e => $"* {e}"));
                var upcomingExcusesString = string.Join("\n", upcomingExcuses.Select(e => $"* {e}"));
                var previousExcusesString = string.Join("\n", previousExcuses.Select(e => $"* {e}"));

                var responseString = string.Empty;

                if (activeExcuses.Count > 0)
                {
                    responseString = "## Here are the currently active excuses:\n" +
                                     activeExcusesString;
                }

                if (upcomingExcuses.Count > 0)
                {
                    responseString += "\n\n" +
                                      "## Here are the upcoming excuses:\n" +
                                      upcomingExcusesString;
                }

                if (previousExcuses.Count > 0)
                {
                    responseString += "\n\n" +
                                      "## Here are the previously active excuses:\n" +
                                      previousExcusesString;
                }

                await RespondAsync(responseString,
                    ephemeral: true).ConfigureAwait(false);
            }
        }
    }
}
