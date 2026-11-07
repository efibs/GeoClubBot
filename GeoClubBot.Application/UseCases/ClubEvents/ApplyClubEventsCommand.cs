using Configuration;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.Observability;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.Club;
using UseCases.UseCases.ClubMembers;
using UseCases.UseCases.MissionBoard;

namespace UseCases.UseCases.ClubEvents;

/// <summary>
/// Applies one batch of events from GeoGuessr's socket (see <see cref="ClubEventPlanner"/>): joins and
/// leaves from the pushed data where it suffices, re-reads where it doesn't. A failing step doesn't stop
/// the others.
/// </summary>
/// <returns>Whether every step succeeded; if not, the caller should ask for a refresh of the clubs.</returns>
public sealed record ApplyClubEventsCommand(IReadOnlyList<ClubPushEvent> Events) : ICommand<bool>;

public sealed partial class ApplyClubEventsHandler(
    IServiceScopeFactory scopeFactory,
    IClubMemberSyncGate memberSyncGate,
    IGeoGuessrCacheInvalidator cacheInvalidator,
    IOptions<GeoGuessrSocketConfiguration> socketConfig,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    ILogger<ApplyClubEventsHandler> logger) : IRequestHandler<ApplyClubEventsCommand, bool>
{
    public async Task<bool> Handle(ApplyClubEventsCommand request, CancellationToken cancellationToken)
    {
        var plan = ClubEventPlanner.Plan(request.Events, ClubEventPlanOptions.From(socketConfig.Value));
        if (plan.IsEmpty)
        {
            return true;
        }

        LogApplying(
            request.Events.Count, plan.ResyncRosters, plan.MemberChanges.Count,
            plan.BoardsToRecord.Count, plan.LevelChecks.Count + plan.LevelUpdates.Count, plan.Renames.Count);
        GeoGuessrSocketMetrics.Batches.Add(1, new KeyValuePair<string, object?>("resync", plan.ResyncRosters));

        // Each step in a scope of its own, so one failing leaves the others their own DbContext.
        var failed = false;
        foreach (var clubId in plan.LevelChecks)
        {
            failed |= !await TryAsync("level check", clubId, new CheckClubLevelCommand(clubId), cancellationToken).ConfigureAwait(false);
        }

        foreach (var (clubId, level) in plan.LevelUpdates)
        {
            failed |= !await TryAsync("level", clubId, new ApplyClubLevelCommand(clubId, level), cancellationToken).ConfigureAwait(false);
        }

        foreach (var (clubId, name) in plan.Renames)
        {
            failed |= !await TryAsync("rename", clubId, new RenameClubCommand(clubId, name), cancellationToken).ConfigureAwait(false);
        }

        failed |= !await ApplyRostersAsync(plan, cancellationToken).ConfigureAwait(false);

        foreach (var clubId in plan.BoardsToRecord)
        {
            cacheInvalidator.InvalidateCurrentMissionBoard(clubId);
            failed |= !await TryAsync("mission claims", clubId, new RecordMissionClaimsCommand(clubId), cancellationToken).ConfigureAwait(false);
        }

        // The website's figures (level, member count) are cached; what changed is read again on the
        // next request.
        foreach (var clubId in ChangedClubs(plan))
        {
            cacheInvalidator.InvalidateClub(clubId);
        }

        return !failed;
    }

    private async Task<bool> ApplyRostersAsync(ClubEventPlan plan, CancellationToken cancellationToken)
    {
        if (plan.ResyncRosters)
        {
            // Every club at once: only then is a leave from one club recognised as half of a switch.
            // The sync takes the member gate itself.
            return await TryAsync("roster sync", null, new SyncClubsCommand(), cancellationToken).ConfigureAwait(false);
        }

        if (plan.MemberChanges.Count == 0)
        {
            return true;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var members = scope.ServiceProvider.GetRequiredService<IClubMemberRepository>();

            await using (await memberSyncGate.AcquireAsync(cancellationToken).ConfigureAwait(false))
            {
                var snapshots = await SnapshotsAsync(plan.MemberChanges, members, cancellationToken).ConfigureAwait(false);
                if (snapshots.Count > 0)
                {
                    await sender.Send(new SaveClubMembersCommand(snapshots), cancellationToken).ConfigureAwait(false);
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogMemberChangesFailed(ex);
            return false;
        }
    }

    /// <summary>
    /// A join is stored as pushed. A leave keeps the stored member's details and only counts if the
    /// stored club is one the member left; otherwise the database is already past it.
    /// </summary>
    private async Task<List<ClubMemberSyncSnapshot>> SnapshotsAsync(
        IReadOnlyList<MemberChange> changes, IClubMemberRepository members, CancellationToken cancellationToken)
    {
        var leavers = changes.OfType<MemberLeft>().ToList();
        var stored = await members
            .ReadClubMembersByUserIdsAsync(leavers.Select(l => l.UserId).ToList(), cancellationToken)
            .ConfigureAwait(false);

        var snapshots = new List<ClubMemberSyncSnapshot>(changes.Count);
        foreach (var change in changes)
        {
            switch (change)
            {
                case MemberJoined joined:
                    snapshots.Add(new ClubMemberSyncSnapshot(
                        joined.UserId, joined.Nickname, joined.ClubId, joined.Xp, joined.JoinedAt));
                    break;

                case MemberLeft left when stored.TryGetValue(left.UserId, out var member)
                                          && member.ClubId is { } storedClub
                                          && left.FromClubs.Contains(storedClub):
                    snapshots.Add(new ClubMemberSyncSnapshot(
                        member.UserId, member.User.Nickname, null, member.Xp, member.JoinedAt));
                    break;

                case MemberLeft left:
                    LogStaleLeave(left.UserId);
                    break;
            }
        }

        return snapshots;
    }

    private IEnumerable<Guid> ChangedClubs(ClubEventPlan plan)
    {
        if (plan.ResyncRosters)
        {
            return geoGuessrConfig.Value.Clubs.Select(c => c.ClubId);
        }

        return plan.MemberChanges
            .SelectMany<MemberChange, Guid>(change => change switch
            {
                MemberJoined joined => [joined.ClubId],
                MemberLeft left => left.FromClubs,
                _ => [],
            })
            .Concat(plan.LevelChecks)
            .Concat(plan.LevelUpdates.Keys)
            .Concat(plan.Renames.Keys)
            .Distinct();
    }

    private async Task<bool> TryAsync(string step, Guid? clubId, IRequest<Unit> command, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The command's own pipeline has already logged the exception as an error.
            LogStepFailed(step, clubId, ex.GetType().Name);
            return false;
        }
    }

    [LoggerMessage(LogLevel.Information,
        "Applying {EventCount} GeoGuessr socket events: roster sync {Resync}, {MemberChanges} member changes, " +
        "{Boards} boards, {Levels} levels, {Renames} renames.")]
    partial void LogApplying(int eventCount, bool resync, int memberChanges, int boards, int levels, int renames);

    [LoggerMessage(LogLevel.Debug, "Ignored a pushed leave of {UserId}: the stored membership is already past it.")]
    partial void LogStaleLeave(string userId);

    [LoggerMessage(LogLevel.Information, "Applying the {Step} of a GeoGuessr socket batch failed (club {ClubId}): {ExceptionType}.")]
    partial void LogStepFailed(string step, Guid? clubId, string exceptionType);

    [LoggerMessage(LogLevel.Error, "Applying the member changes of a GeoGuessr socket batch failed.")]
    partial void LogMemberChangesFailed(Exception ex);
}
