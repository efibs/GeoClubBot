using Entities;

namespace UseCases.OutputPorts.GeoGuessr;

/// <summary>
/// Reads a club's weekly mission board, cached briefly because reminders, alerts and commands all
/// look at it.
/// </summary>
public interface IClubMissionBoardReader
{
    /// <summary>The running board week; null when GeoGuessr has none or the read failed.</summary>
    Task<ClubMissionBoardWeek?> ReadCurrentAsync(Guid clubId, CancellationToken cancellationToken = default);

    /// <summary>The previous board week; null when GeoGuessr has none or the read failed.</summary>
    Task<ClubMissionBoardWeek?> ReadPreviousAsync(Guid clubId, CancellationToken cancellationToken = default);
}
