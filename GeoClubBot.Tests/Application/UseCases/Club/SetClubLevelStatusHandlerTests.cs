using NSubstitute;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.Club;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.Club;

public sealed class SetClubLevelStatusHandlerTests
{
    [Fact]
    public async Task PutsTheLevelIntoTheBotStatus()
    {
        var updater = Substitute.For<IDiscordStatusUpdater>();

        await new SetClubLevelStatusHandler(updater).Handle(new SetClubLevelStatusCommand(12), CancellationToken.None);

        await updater.Received(1).UpdateStatusAsync("Level 12 club!", Arg.Any<CancellationToken>());
    }
}
