using Discord.Interactions;
using UseCases.InputPorts.Excuses;

namespace Infrastructure.InputAdapters.Interactions;

public partial class ActivityModule
{
    public partial class ActivityExcuseModule(
        IAddExcuseUseCase addExcuseUseCase,
        IUpdateExcuseUseCase updateExcuseUseCase,
        IRemoveExcuseUseCase removeExcuseUseCase,
        IReadExcusesUseCase readExcusesUseCase)
    {
        [SlashCommand("add", "Add an excuse for a player")]
        public async Task AddExcuseAsync(string memberNickname,
            [Summary(description: "From date in format YYYY-MM-DD")]
            DateTime from,
            [Summary(description: "To date in format YYYY-MM-DD")]
            DateTime to)
        {
            // Add one day to the to time and subtract a tick so that the 
            // day still counts for the excuse
            to = to.AddDays(1).AddTicks(-1);
            
            // Specify the date times as utc
            from = DateTime.SpecifyKind(from, DateTimeKind.Utc);
            to = DateTime.SpecifyKind(to, DateTimeKind.Utc);
            
            // Check if the dates are in wrong order
            if (from >= to)
            {
                await RespondAsync($"Excuse could not be added: The given from date lies after the given to date.",
                    ephemeral: true).ConfigureAwait(false);
                
                return;
            }
            
            // Add the excuse
            var excuseGuid = await addExcuseUseCase.AddExcuseAsync(memberNickname, from, to).ConfigureAwait(false);

            // If the excuse could not be added
            if (excuseGuid == null)
            {
                await RespondAsync($"Excuse could not be added for player '{memberNickname}'. Is the nickname wrong?",
                    ephemeral: true).ConfigureAwait(false);
            }
            else
            {
                await RespondAsync(
                    $"Excuse with id {excuseGuid} for the time range **{from:D}** to **{to:D}** was added to player " +
                    $"**{memberNickname}**.",
                    ephemeral: true).ConfigureAwait(false);
            }
        }

        [SlashCommand("update", "Update an excuse for a player")]
        public async Task UpdateExcuseAsync(string excuseId,
            [Summary(description: "The new from date in format YYYY-MM-DD")]
            DateTime from,
            [Summary(description: "The new to date in format YYYY-MM-DD")]
            DateTime to)
        {
            // Parse the excuse id
            var parseSuccessful = Guid.TryParse(excuseId, out var excuseIdGuid);

            // If the parse was not successful
            if (!parseSuccessful)
            {
                // Respond
                await RespondAsync($"Invalid GUID '{excuseId}'. Please enter a valid GUID.", ephemeral: true).ConfigureAwait(false);
                return;
            }
            
            // Add one day to the to time and subtract a tick so that the 
            // day still counts for the excuse
            to = to.AddDays(1).AddTicks(-1);
            
            // Specify the date times as utc
            from = DateTime.SpecifyKind(from, DateTimeKind.Utc);
            to = DateTime.SpecifyKind(to, DateTimeKind.Utc);
            
            // Check if the dates are in wrong order
            if (from >= to)
            {
                await RespondAsync($"Excuse could not be updated: The given from date lies after the given to date.",
                    ephemeral: true).ConfigureAwait(false);
                
                return;
            }
            
            // Update the excuse
            var updatedExcuse = await updateExcuseUseCase.UpdateExcuseAsync(excuseIdGuid, from, to).ConfigureAwait(false);

            // If the excuse could not be updated
            if (updatedExcuse == null)
            {
                await RespondAsync($"Excuse '{excuseIdGuid}' does not exist.",
                    ephemeral: true).ConfigureAwait(false);
            }
            else
            {
                await RespondAsync(
                    $"Excuse with id {excuseIdGuid} was updated to the time range **{updatedExcuse.From:D}** to **{updatedExcuse.To:D}**.",
                    ephemeral: true).ConfigureAwait(false);
            }
        }
        
        [SlashCommand("remove", "Remove an excuse for a player given its id")]
        public async Task RemoveExcuseAsync(string excuseId)
        {
            // Parse the excuse id
            var parseSuccessful = Guid.TryParse(excuseId, out var excuseIdGuid);

            // If the parse was not successful
            if (!parseSuccessful)
            {
                // Respond
                await RespondAsync($"Invalid GUID '{excuseId}'. Please enter a valid GUID.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            // Remove the excuse
            var successful = await removeExcuseUseCase.RemoveExcuseAsync(excuseIdGuid).ConfigureAwait(false);

            // If the remove was successful
            if (successful)
            {
                // Respond
                await RespondAsync($"Excuse with id {excuseId} successfully removed", ephemeral: true).ConfigureAwait(false);
            }
            else
            {
                // Respond
                await RespondAsync($"There is no excuse with id {excuseId}", ephemeral: true).ConfigureAwait(false);
            }
        }

        [SlashCommand("read", "Read the excuses for a player")]
        public async Task ReadExcusesAsync(string memberNickname)
        {
            // Read the excuses
            var excuses = await readExcusesUseCase.ReadExcusesAsync(memberNickname).ConfigureAwait(false);

            // If there are no excuses
            if (excuses.Count == 0)
            {
                // Respond
                await RespondAsync($"The player {memberNickname} has no excuses.", ephemeral: true).ConfigureAwait(false);
            }
            else
            {
                // Build excuses string
                var excusesString = string.Join("\n", excuses.Select(e => $"* {e}"));

                // Respond
                await RespondAsync($"The player {memberNickname} has the following excuses:\n{excusesString}",
                    ephemeral: true).ConfigureAwait(false);
            }
        }
        
        [SlashCommand("read-all", "Read all excuses in the system")]
        public async Task ReadExcusesAsync()
        {
            // Read the excuses
            var excuses = await readExcusesUseCase.ReadExcusesAsync().ConfigureAwait(false);

            // If there are no excuses
            if (excuses.Count == 0)
            {
                // Respond
                await RespondAsync($"There are currently no excuses in the system.", ephemeral: true).ConfigureAwait(false);
            }
            else
            {
                // Build excuses string
                var excusesString = string.Join("\n", excuses.Select(e => $"* {e.ToStringWithPlayerName()}"));

                // Respond
                await RespondAsync($"The following excuses are currently entered in the system:\n{excusesString}",
                    ephemeral: true).ConfigureAwait(false);
            }
        }
    }
}