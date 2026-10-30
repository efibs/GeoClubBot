using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;

namespace UseCases.UseCases.MissionBoard;

/// <summary>
/// Copies the claims on every club's current and previous mission board into
/// <see cref="MissionClaim"/>s (see <see cref="MissionClaimTrackingConfiguration"/>). The previous
/// board is read too, so missions completed after the last run of a week — or during downtime —
/// still get their final state.
/// </summary>
public sealed record RecordMissionClaimsCommand : ICommand;

public sealed partial class RecordMissionClaimsHandler(
    IClubMissionBoardReader boardReader,
    IMissionClaimRepository claims,
    IOptions<MissionClaimTrackingConfiguration> config,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    ILogger<RecordMissionClaimsHandler> logger) : IRequestHandler<RecordMissionClaimsCommand, Unit>
{
    public async Task<Unit> Handle(RecordMissionClaimsCommand request, CancellationToken cancellationToken)
    {
        var options = config.Value;
        if (!options.Enabled)
        {
            return Unit.Value;
        }

        foreach (var clubId in geoGuessrConfig.Value.Clubs.Select(c => c.ClubId))
        {
            try
            {
                var previous = await boardReader.ReadPreviousAsync(clubId, cancellationToken).ConfigureAwait(false);
                await RecordAsync(clubId, previous, cancellationToken).ConfigureAwait(false);

                var current = await boardReader.ReadCurrentAsync(clubId, cancellationToken).ConfigureAwait(false);
                await RecordAsync(clubId, current, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One club failing must not keep the other clubs' claims back.
                LogClubRecordFailed(ex, clubId);
            }
        }

        await claims
            .DeleteClaimedBeforeAsync(DateTimeOffset.UtcNow - options.Retention, cancellationToken)
            .ConfigureAwait(false);

        return Unit.Value;
    }

    private async Task RecordAsync(Guid clubId, ClubMissionBoardWeek? board, CancellationToken cancellationToken)
    {
        if (board is null)
        {
            return;
        }

        var recorded = (await claims
                .ReadForUpdateByPeriodAsync(clubId, board.PeriodStart, cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(c => c.MissionId);

        var added = 0;
        var updated = 0;
        foreach (var tile in board.AllTiles)
        {
            if (tile is not { ClaimedBy: not null, ClaimedAt: not null })
            {
                continue;
            }

            var userId = tile.ClaimedBy;
            var claimedAt = tile.ClaimedAt.Value;
            var completedAt = tile.Completed ? tile.CompletedAt : null;
            if (recorded.TryGetValue(tile.MissionId, out var claim))
            {
                if (claim.Update(userId, claimedAt, completedAt))
                {
                    updated++;
                }
            }
            else
            {
                claims.Add(MissionClaim.Create(
                    clubId, board.PeriodStart, board.PeriodEnd, tile.MissionId, userId, claimedAt, completedAt));
                added++;
            }
        }

        if (added > 0 || updated > 0)
        {
            LogRecorded(added, updated, clubId, board.PeriodStart);
        }
    }

    [LoggerMessage(LogLevel.Debug, "Recorded {Added} new and {Updated} changed mission claims of club {ClubId} for the board week of {PeriodStart:u}.")]
    partial void LogRecorded(int added, int updated, Guid clubId, DateTimeOffset periodStart);

    [LoggerMessage(LogLevel.Error, "Recording the mission claims of club {ClubId} failed.")]
    partial void LogClubRecordFailed(Exception ex, Guid clubId);
}
