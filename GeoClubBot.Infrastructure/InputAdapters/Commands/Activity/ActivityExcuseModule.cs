using Discord.Interactions;
using UseCases.InputPorts;

namespace Infrastructure.InputAdapters.Commands;

public partial class ActivityModule
{
    public partial class ActivityExcuseModule(
        IAddExcuseUseCase addExcuseUseCase,
        IRemoveExcuseUseCase removeExcuseUseCase,
        IReadExcusesUseCase readExcusesUseCase,
        IIsPlayerTrackedUseCase isPlayerTrackedUseCase)
    {
        [SlashCommand("add", "Add an excuse for a player")]
        public async Task AddExcuseAsync(string memberNickname, 
            [Summary(description: "From date in format YYYY-MM-DD")] DateTime from, 
            [Summary(description: "To date in format YYYY-MM-DD")] DateTime to)
        {
            // Check if the player is being tracked
            var playerIsTracked = await isPlayerTrackedUseCase.IsPlayerTrackedAsync(memberNickname);

            // Add the excuse
            var excuseGuid = await addExcuseUseCase.AddExcuseAsync(memberNickname, from, to);

            // The player is not being tracked appendix
            var playerNotTrackedAppendix = !playerIsTracked
                ? $"\n\n**P.s.: The player '{memberNickname}' is currently not being tracked. Please double check that " +
                  $"the nickname is correct. This is expected only if the player joined the club after the last activity check.**"
                : "";

            // Respond
            await RespondAsync(
                $"Excuse with id {excuseGuid} for the time range {from:D} to {to:D} was added to player " +
                $"{memberNickname}.{playerNotTrackedAppendix}",
                ephemeral: true);
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
                await RespondAsync($"Invalid GUID '{excuseId}'. Please enter a valid GUID.", ephemeral: true);
                return;
            }
            
            // Remove the excuse
            var successful = await removeExcuseUseCase.RemoveExcuseAsync(excuseIdGuid);
            
            // If the remove was successful
            if (successful)
            {
                // Respond
                await RespondAsync($"Excuse with id {excuseId} successfully removed", ephemeral: true);
            }
            else
            {
                // Respond
                await RespondAsync($"There is no excuse with id {excuseId}", ephemeral: true);
            }
        }

        [SlashCommand("read", "Read the excuses for a player")]
        public async Task ReadExcusesAsync(string memberNickname)
        {
            // Read the excuses
            var excuses = await readExcusesUseCase.ReadExcusesAsync(memberNickname);
            
            // If there are no excuses
            if (excuses.Count == 0)
            {
                // Respond
                await RespondAsync($"The player {memberNickname} has no excuses.", ephemeral: true);
            }
            else
            {
                // Build excuses string
                var excusesString = string.Join("\n", excuses.Select(e => $"* {e}"));
                
                // Respond
                await RespondAsync($"The player {memberNickname} has the following excuses:\n{excusesString}", 
                    ephemeral: true);
            }
        }
    }
}