using Discord.Interactions;
using UseCases.InputPorts;

namespace Infrastructure.InputAdapters.Commands;

public partial class ActivityModule
{
    public partial class ActivityStrikeModule(
        IReadMemberStrikesUseCase readMemberStrikesUseCase,
        IRevokeStrikeUseCase revokeStrikeUseCase,
        IUnrevokeStrikeUseCase unrevokeStrikeUseCase)
    {
        [SlashCommand("read", "Read the strikes a player currently has")]
        public async Task ReadNumStrikesAsync(string memberNickname)
        {
            // Read the number of strikes
            var strikeStatus = await readMemberStrikesUseCase.ReadMemberStrikesAsync(memberNickname);

            // If the player has a status set
            if (strikeStatus == null)
            {
                // Respond
                await RespondAsync($"There is no player with the nickname {memberNickname} currently being tracked. " +
                                   $"Either the nickname is incorrect or the member just joined and is not yet being tracked.",
                    ephemeral: true);
                return;
            }

            // If the player has no strikes
            if (strikeStatus.Strikes.Count == 0)
            {
                // Respond
                await RespondAsync($"The player {memberNickname} currently has no strikes.",
                    ephemeral: true);
                return;
            }
            
            // Build the list of strikes
            var strikesListString = string.Join("\n", strikeStatus.Strikes.Select(s => $"- {s}"));
                
            // Respond
            await RespondAsync($"The player {memberNickname} currently has {strikeStatus.NumActiveStrikes} active strikes:\n{strikesListString}",
                ephemeral: true);
        }

        [SlashCommand("revoke", "Revoke a strike")]
        public async Task RevokeStrikeAsync(string strikeId)
        {
            // Parse the id
            var parseSuccessful = Guid.TryParse(strikeId, out var strikeIdGuid);

            // If the parse was not successful
            if (!parseSuccessful)
            {
                // Respond
                await RespondAsync($"Invalid GUID '{strikeId}'. Please enter a valid GUID.", ephemeral: true);
                return;
            }
            
            // Revoke the strike
            var revokeSuccessful = await revokeStrikeUseCase.RevokeStrikeAsync(strikeIdGuid);
            
            // If the revoke was successful
            if (revokeSuccessful)
            {
                // Respond
                await RespondAsync($"Strike with id {strikeId} was successfully revoked.", ephemeral: true);
            }
            else
            {
                // Respond
                await RespondAsync($"Strike with id {strikeId} could not be revoked.", ephemeral: true);
            }
        }
        
        [SlashCommand("unrevoke", "Remove a revocation of a strike")]
        public async Task UnrevokeStrikeAsync(string strikeId)
        {
            // Parse the id
            var parseSuccessful = Guid.TryParse(strikeId, out var strikeIdGuid);

            // If the parse was not successful
            if (!parseSuccessful)
            {
                // Respond
                await RespondAsync($"Invalid GUID '{strikeId}'. Please enter a valid GUID.", ephemeral: true);
                return;
            }
            
            // Revoke the strike
            var revokeSuccessful = await unrevokeStrikeUseCase.UnrevokeStrikeAsync(strikeIdGuid);
            
            // If the revoke was successful
            if (revokeSuccessful)
            {
                // Respond
                await RespondAsync($"Revocation of strike with id {strikeId} was successfully removed.", ephemeral: true);
            }
            else
            {
                // Respond
                await RespondAsync($"Revocation of strike with id {strikeId} could not be removed.", ephemeral: true);
            }
        }
    }
}