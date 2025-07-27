using Discord.Interactions;
using UseCases.InputPorts;

namespace Infrastructure.InputAdapters.Commands;

public partial class ActivityModule
{
    public partial class ActivityStrikeModule(
        IReadMemberStrikesUseCase readMemberStrikesUseCase)
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
    }
}