using Discord.Interactions;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.UseCases.Excuses;

namespace GeoClubBot.Discord.InputAdapters.Interactions;

public partial class ActivityModule
{
    public partial class ActivityExcuseModule(
        ISender mediator,
        ILogger<ActivityExcuseModule> logger) : ClubBotInteractionModule(mediator, logger)
    {
        [SlashCommand("add", "Add an excuse for a player")]
        public async Task AddExcuseAsync(string memberNickname,
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

            var excuseGuid = await Mediator
                .Send(new AddExcuseCommand(memberNickname, from, to))
                .ConfigureAwait(false);

            if (excuseGuid == null)
            {
                await RespondAsync($"Excuse could not be added for player '{memberNickname}'. Is the nickname wrong?",
                    ephemeral: true).ConfigureAwait(false);
            }
            else
            {
                await RespondAsync(
                    $"Excuse with id {excuseGuid} for the time range **{from:D}** to **{to:D}** was added to player " +
                    $"**{memberNickname}**.").ConfigureAwait(false);
            }
        }

        [SlashCommand("update", "Update an excuse for a player")]
        public async Task UpdateExcuseAsync(string excuseId,
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

            var updatedExcuse = await Mediator
                .Send(new UpdateExcuseCommand(excuseIdGuid, from, to))
                .ConfigureAwait(false);

            if (updatedExcuse == null)
            {
                await RespondAsync($"Excuse '{excuseIdGuid}' does not exist.",
                    ephemeral: true).ConfigureAwait(false);
            }
            else
            {
                await RespondAsync(
                    $"Excuse with id {excuseIdGuid} was updated to the time range **{updatedExcuse.From:D}** to **{updatedExcuse.To:D}**.")
                    .ConfigureAwait(false);
            }
        }

        [SlashCommand("remove", "Remove an excuse for a player given its id")]
        public async Task RemoveExcuseAsync(string excuseId)
        {
            if (!Guid.TryParse(excuseId, out var excuseIdGuid))
            {
                await RespondAsync($"Invalid GUID '{excuseId}'. Please enter a valid GUID.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            var successful = await Mediator.Send(new RemoveExcuseCommand(excuseIdGuid)).ConfigureAwait(false);

            await RespondAsync(
                successful
                    ? $"Excuse with id {excuseId} successfully removed"
                    : $"There is no excuse with id {excuseId}",
                ephemeral: !successful).ConfigureAwait(false);
        }

        [SlashCommand("read", "Read the excuses for a player")]
        public async Task ReadExcusesAsync(string memberNickname)
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
    }
}
