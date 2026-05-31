using MediatR;
using UseCases.Abstractions;
using UseCases.OutputPorts.Discord;

namespace UseCases.UseCases.Club;

public sealed record SetClubLevelStatusCommand(int Level) : ICommand;

public sealed class SetClubLevelStatusHandler(IDiscordStatusUpdater discordStatusUpdater)
    : IRequestHandler<SetClubLevelStatusCommand, Unit>
{
    public async Task<Unit> Handle(SetClubLevelStatusCommand request, CancellationToken cancellationToken)
    {
        var newStatus = $"Level {request.Level} club!";
        await discordStatusUpdater.UpdateStatusAsync(newStatus, cancellationToken).ConfigureAwait(false);
        return Unit.Value;
    }
}
