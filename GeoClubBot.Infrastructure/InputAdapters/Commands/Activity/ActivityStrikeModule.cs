using Discord.Interactions;
using UseCases.InputPorts;

namespace Infrastructure.InputAdapters.Commands;

public partial class ActivityModule
{
    public partial class ActivityStrikeModule(
        IAddStrikeUseCase addStrikeUseCase,
        IReadMemberStrikesUseCase readMemberStrikesUseCase,
        IRevokeStrikeUseCase revokeStrikeUseCase,
        IUnrevokeStrikeUseCase unrevokeStrikeUseCase)
    {
        [SlashCommand("add", "Create a new strike for a player")]
        public async Task CreateStrikeAsync(string memberNickname,
            [Summary(description: "Strike date in format YYYY-MM-DD")]
            DateTime strikeDate)
        {
            // Specify the date time as utc
            strikeDate = DateTime.SpecifyKind(strikeDate, DateTimeKind.Utc);
            
            // Add the strike
            var strikeId = await addStrikeUseCase.AddStrikeAsync(memberNickname, strikeDate);

            // If the player has a status set
            if (strikeId == null)
            {
                await RespondAsync($"Excuse could not be added for player '{memberNickname}'. Is the nickname wrong?",
                    ephemeral: true);
            }
            else
            {
                await RespondAsync(
                    $"Strike with id {strikeId} was added to player **{memberNickname}**.",
                    ephemeral: true);
            }
        }
        
        [SlashCommand("read", "Read the strikes a player currently has")]
        public async Task ReadStrikesAsync(string memberNickname)
        {
            // Read the strikes
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