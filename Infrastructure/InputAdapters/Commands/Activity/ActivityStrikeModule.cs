using Discord.Interactions;
using UseCases.InputPorts;

namespace Infrastructure.InputAdapters.Commands;

public partial class ActivityModule
{
    public partial class ActivityStrikeModule(
        IReadMemberNumStrikesUseCase readMemberNumStrikesUseCase,
        IWriteMemberNumStrikesUseCase writeMemberNumStrikesUseCase)
    {
        [SlashCommand("read", "Read the number of strikes a player currently has")]
        public async Task ReadNumStrikesAsync(string memberNickname)
        {
            // Read the number of strikes
            var numStrikes = await readMemberNumStrikesUseCase.ReadMemberNumStrikesAsync(memberNickname);

            // If the player has a status set
            if (numStrikes.HasValue)
            {
                // Respond
                await RespondAsync($"The player {memberNickname} currently has {numStrikes} strikes",
                    ephemeral: true);
            }
            else
            {
                // Respond
                await RespondAsync($"There is no player with the nickname {memberNickname} currently being tracked. " +
                                   $"Either the nickname is incorrect or the member just joined and is not yet being tracked.",
                    ephemeral: true);
            }
        }

        [SlashCommand("write", "Overwrite the number of strikes a player currently has")]
        public async Task WriteNumStrikesAsync(string memberNickname, int numStrikes)
        {
            // Write the number of strikes
            var successful = await writeMemberNumStrikesUseCase.WriteNumStrikesAsync(memberNickname, numStrikes);

            // If writing was successful
            if (successful)
            {
                // Respond
                await RespondAsync(
                    $"The number of strikes of player {memberNickname} successfully was set to {numStrikes}",
                    ephemeral: true);
            }
            else
            {
                // Respond
                await RespondAsync($"There is no player with the nickname {memberNickname} currently being tracked. " +
                                   $"Either the nickname is incorrect or the member just joined and is not yet being tracked.",
                    ephemeral: true);
            }
        }
    }
}