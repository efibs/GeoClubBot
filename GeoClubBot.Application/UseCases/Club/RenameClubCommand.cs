using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;

namespace UseCases.UseCases.Club;

/// <summary>
/// Stores a club's new name, as pushed by GeoGuessr's socket when the club was renamed. The scheduled
/// roster sync renames too, from the club it reads; this only saves waiting for it.
/// </summary>
public sealed record RenameClubCommand(Guid ClubId, string Name) : ICommand;

public sealed partial class RenameClubHandler(
    IClubRepository clubs,
    ILogger<RenameClubHandler> logger) : IRequestHandler<RenameClubCommand, Unit>
{
    public async Task<Unit> Handle(RenameClubCommand request, CancellationToken cancellationToken)
    {
        var club = await clubs.ReadForUpdateByIdAsync(request.ClubId, cancellationToken).ConfigureAwait(false);
        if (club is null)
        {
            // Not stored yet: the first roster sync creates it with its current name.
            return Unit.Value;
        }

        if (club.Name != request.Name)
        {
            LogRenamed(request.ClubId, request.Name);
            club.Rename(request.Name);
        }

        // Saved by the unit-of-work behaviour.
        return Unit.Value;
    }

    [LoggerMessage(LogLevel.Information, "Club {ClubId} is now called {Name}.")]
    partial void LogRenamed(Guid clubId, string name);
}
