using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.MissionBoard;

/// <summary>
/// Copies the claims on every club's current and previous mission board into
/// <see cref="MissionClaim"/>s (see <see cref="MissionClaimTrackingConfiguration"/>). The previous
/// board is read too, so missions completed after the last run of a week — or during downtime —
/// still get their final state.
/// </summary>
/// <param name="ClubId">Only this club, e.g. after its board changed; <c>null</c> for every club.</param>
public sealed record RecordMissionClaimsCommand(Guid? ClubId = null) : ICommand;

public sealed partial class RecordMissionClaimsHandler(
    IClubMissionBoardReader boardReader,
    IMissionClaimRepository claims,
    IServiceScopeFactory scopeFactory,
    IOptions<MissionClaimTrackingConfiguration> config,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    ILogger<RecordMissionClaimsHandler> logger) : IRequestHandler<RecordMissionClaimsCommand, Unit>
{
    // The scheduled run and a board change pushed over the socket can record the same club at the
    // same time; both would insert the same new claim and the second save would break the unique
    // (club, week, mission) index. Static, because handlers are resolved per scope.
    private static readonly KeyedAsyncLock<Guid> ClubLocks = new();

    public async Task<Unit> Handle(RecordMissionClaimsCommand request, CancellationToken cancellationToken)
    {
        var options = config.Value;
        if (!options.Enabled)
        {
            return Unit.Value;
        }

        var clubIds = geoGuessrConfig.Value.Clubs
            .Select(c => c.ClubId)
            .Where(id => request.ClubId is null || id == request.ClubId);

        foreach (var clubId in clubIds)
        {
            try
            {
                await RecordClubAsync(clubId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One club failing must not keep the other clubs' claims back.
                LogClubRecordFailed(ex, clubId);
            }
        }

        // Only the full run prunes; a single club's update has no reason to.
        if (request.ClubId is null)
        {
            await claims
                .DeleteClaimedBeforeAsync(DateTimeOffset.UtcNow - options.Retention, cancellationToken)
                .ConfigureAwait(false);
        }

        return Unit.Value;
    }

    /// <summary>
    /// Records one club in a scope of its own and saves it under the club's lock, so a failed save
    /// leaves nothing pending for the next club and a concurrent run sees this one's claims.
    /// </summary>
    private async Task RecordClubAsync(Guid clubId, CancellationToken cancellationToken)
    {
        using var clubLock = await ClubLocks.AcquireAsync(clubId, cancellationToken).ConfigureAwait(false);
        await using var scope = scopeFactory.CreateAsyncScope();
        var scopedClaims = scope.ServiceProvider.GetRequiredService<IMissionClaimRepository>();
        var scopedUnitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var previous = await boardReader.ReadPreviousAsync(clubId, cancellationToken).ConfigureAwait(false);
        await RecordAsync(scopedClaims, clubId, previous, cancellationToken).ConfigureAwait(false);

        var current = await boardReader.ReadCurrentAsync(clubId, cancellationToken).ConfigureAwait(false);
        await RecordAsync(scopedClaims, clubId, current, cancellationToken).ConfigureAwait(false);

        await scopedUnitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RecordAsync(
        IMissionClaimRepository clubClaims, Guid clubId, ClubMissionBoardWeek? board, CancellationToken cancellationToken)
    {
        if (board is null)
        {
            return;
        }

        var recorded = (await clubClaims
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
                clubClaims.Add(MissionClaim.Create(
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
