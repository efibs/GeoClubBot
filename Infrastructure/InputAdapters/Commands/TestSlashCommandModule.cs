using Discord.Interactions;
using Microsoft.Extensions.Logging;

namespace Infrastructure.InputAdapters.Commands;

public class TestSlashCommandModule(ILogger<TestSlashCommandModule> logger)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("test-slash-command", "Echo an input")]
    public async Task Echo(string input)
    {
        logger.LogDebug("Echoing '{input}'", input);

        await RespondAsync(input);
    }
}