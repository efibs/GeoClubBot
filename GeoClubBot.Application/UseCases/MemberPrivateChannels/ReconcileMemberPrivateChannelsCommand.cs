using Configuration;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.MemberPrivateChannels;

/// <summary>
/// Puts every member's private channel where it belongs: into a category of the member's club (created
/// as needed), into the archive for members in no club, recreated when it is missing, and with exactly
/// the permissions of <see cref="PrivateChannelAccessPolicy"/>. Migrates the old single shared category,
/// recovers channels whose creation failed, and heals whatever drifted. Runs after the start-up sync and
/// on demand; <see cref="DryRun"/> only reports what it would do.
/// </summary>
public sealed record ReconcileMemberPrivateChannelsCommand(bool DryRun) : ICommand<Result<PrivateChannelReconcileReport>>;

/// <param name="Repairs">The nicknames per repair, done (or, in a dry run, to do).</param>
/// <param name="AccessFixed">Nicknames whose channel permissions were (or would be) rewritten.</param>
/// <param name="Failures">"nickname: error" for every member whose repair failed.</param>
/// <param name="CategoriesToCreate">Dry run: new categories needed per club name. Real run: categories created.</param>
/// <param name="EmptiedCategoryIds">Categories outside the bot's own that held moved channels and are empty now (the old shared one).</param>
public sealed record PrivateChannelReconcileReport(
    bool DryRun,
    IReadOnlyDictionary<PrivateChannelRepair, IReadOnlyList<string>> Repairs,
    IReadOnlyList<string> AccessFixed,
    IReadOnlyList<string> Failures,
    IReadOnlyDictionary<string, int> CategoriesToCreate,
    int CategoryAccessFixed,
    int CategoriesPruned,
    IReadOnlyList<ulong> EmptiedCategoryIds)
{
    public bool NothingToDo =>
        Repairs.Count == 0 && AccessFixed.Count == 0 && Failures.Count == 0
        && CategoriesToCreate.Count == 0 && CategoryAccessFixed == 0 && CategoriesPruned == 0;
}

public sealed partial class ReconcileMemberPrivateChannelsHandler(
    MemberPrivateChannelManager manager,
    IClubMemberRepository clubMembers,
    IClubRepository clubs,
    IUnitOfWork unitOfWork,
    IOptions<MemberPrivateChannelsConfiguration> options,
    ILogger<ReconcileMemberPrivateChannelsHandler> logger)
    : IRequestHandler<ReconcileMemberPrivateChannelsCommand, Result<PrivateChannelReconcileReport>>
{
    public Task<Result<PrivateChannelReconcileReport>> Handle(ReconcileMemberPrivateChannelsCommand request,
        CancellationToken cancellationToken) =>
        PrivateChannelPlacementLock.RunAsync(() => ReconcileAsync(request.DryRun, cancellationToken), cancellationToken);

    private async Task<Result<PrivateChannelReconcileReport>> ReconcileAsync(bool dryRun,
        CancellationToken cancellationToken)
    {
        var config = options.Value;
        var placement = await manager.LoadAsync(cancellationToken).ConfigureAwait(false);
        var members = await clubMembers.ReadClubMembersAsync(cancellationToken).ConfigureAwait(false);
        var plan = PrivateChannelReconcilePlanner.Plan(members, placement, config.ArchiveCategoryId, config.AdminRoleIds);

        var clubNames = (await clubs.ReadAllClubsAsync(cancellationToken).ConfigureAwait(false))
            .GroupBy(c => c.ClubId)
            .ToDictionary(g => g.Key, g => g.First().Name);
        // Keyed by name for the report; two clubs of the same name are added up rather than clash.
        Dictionary<string, int> CountByClubName(IEnumerable<(Guid ClubId, int Count)> counts) =>
            counts
                .GroupBy(c => clubNames.GetValueOrDefault(c.ClubId) ?? c.ClubId.ToString())
                .ToDictionary(g => g.Key, g => g.Sum(c => c.Count));

        if (dryRun)
        {
            return new PrivateChannelReconcileReport(
                DryRun: true,
                Repairs: GroupRepairs(plan.Where(p => p.Repair is not null).Select(p => (p.Repair!.Value, p.Member.User.Nickname))),
                AccessFixed: plan.Where(p => p.FixAccess).Select(p => p.Member.User.Nickname).ToList(),
                Failures: [],
                CategoriesToCreate: CountByClubName(PrivateChannelReconcilePlanner
                    .CategoriesToCreate(plan, placement, config.MaxChannelsPerCategory)
                    .Where(kv => kv.Value > 0)
                    .Select(kv => (kv.Key, kv.Value))),
                CategoryAccessFixed: 0,
                CategoriesPruned: 0,
                EmptiedCategoryIds: []);
        }

        var categoriesBefore = placement.Categories.Select(c => c.CategoryId).ToHashSet();
        var done = new List<(PrivateChannelRepair, string)>();
        var accessFixed = new List<string>();
        var failures = new List<string>();
        var leftCategories = new HashSet<ulong>();

        foreach (var item in plan)
        {
            var member = item.Member;
            var nickname = member.User.Nickname;

            try
            {
                if (member.PrivateTextChannelId is { } channelId && placement.ParentOf(channelId) is { } parent
                    && !placement.IsBotCategory(parent) && parent != config.ArchiveCategoryId)
                {
                    leftCategories.Add(parent);
                }

                var error = item.Repair is { } repair
                    ? await RepairAsync(placement, item, repair, cancellationToken).ConfigureAwait(false)
                    : Error.None;

                if (error != Error.None)
                {
                    failures.Add($"{nickname}: {error.Message}");
                    LogRepairFailed(logger, nickname, item.Repair, error.Message);
                }
                else
                {
                    if (item.Repair is { } r)
                    {
                        done.Add((r, nickname));
                    }

                    if (item.FixAccess && await manager.EnsureAccessAsync(placement, member, cancellationToken).ConfigureAwait(false))
                    {
                        accessFixed.Add(nickname);
                    }
                }

                // Saved per member, so whatever succeeded is remembered even if a later member throws.
                await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add($"{nickname}: {ex.Message}");
                LogRepairThrew(logger, nickname, item.Repair, ex);
            }
        }

        var categoryAccessFixed = await manager.EnsureCategoryAccessAsync(placement, cancellationToken).ConfigureAwait(false);
        var pruned = await manager.PruneEmptyCategoriesAsync(placement, cancellationToken).ConfigureAwait(false);

        var created = CountByClubName(placement.Categories
            .Where(c => !categoriesBefore.Contains(c.CategoryId))
            .Select(c => (c.ClubId, 1)));

        var report = new PrivateChannelReconcileReport(
            DryRun: false,
            Repairs: GroupRepairs(done),
            AccessFixed: accessFixed,
            Failures: failures,
            CategoriesToCreate: created,
            CategoryAccessFixed: categoryAccessFixed,
            CategoriesPruned: pruned,
            EmptiedCategoryIds: leftCategories.Where(id => placement.CountIn(id) == 0).Order().ToList());

        LogReconciled(logger, done.Count, accessFixed.Count, failures.Count, created.Values.Sum());
        return report;
    }

    private async Task<Error> RepairAsync(PrivateChannelPlacement placement, PlannedPrivateChannelRepair item,
        PrivateChannelRepair repair, CancellationToken cancellationToken)
    {
        var member = item.Member;

        switch (repair)
        {
            case PrivateChannelRepair.Create:
                return (await manager.CreateAsync(placement, member, cancellationToken).ConfigureAwait(false)).Error;

            case PrivateChannelRepair.Restore:
                return (await manager.RestoreAsync(placement, member, cancellationToken).ConfigureAwait(false)).Error;

            case PrivateChannelRepair.MoveToClub:
                return (await manager.MoveToClubAsync(placement, member, cancellationToken).ConfigureAwait(false)).Error;

            case PrivateChannelRepair.Archive:
            case PrivateChannelRepair.MoveToArchive:
                return (await manager.ArchiveAsync(placement, member, cancellationToken).ConfigureAwait(false)).Error;

            case PrivateChannelRepair.Forget:
                var tracked = await clubMembers
                    .ReadForUpdateByUserIdAsync(member.UserId, cancellationToken)
                    .ConfigureAwait(false);
                tracked?.SetPrivateTextChannelId(null);
                return Error.None;

            default:
                throw new ArgumentOutOfRangeException(nameof(repair), repair, null);
        }
    }

    private static IReadOnlyDictionary<PrivateChannelRepair, IReadOnlyList<string>> GroupRepairs(
        IEnumerable<(PrivateChannelRepair Repair, string Nickname)> repairs) =>
        repairs
            .GroupBy(r => r.Repair)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(r => r.Nickname).ToList());

    [LoggerMessage(LogLevel.Warning, "Reconcile could not {Repair} the private channel of '{clubMemberNickname}': {Error}")]
    static partial void LogRepairFailed(ILogger<ReconcileMemberPrivateChannelsHandler> logger, string clubMemberNickname,
        PrivateChannelRepair? repair, string error);

    [LoggerMessage(LogLevel.Error, "Reconcile failed on the private channel of '{clubMemberNickname}' ({Repair}).")]
    static partial void LogRepairThrew(ILogger<ReconcileMemberPrivateChannelsHandler> logger, string clubMemberNickname,
        PrivateChannelRepair? repair, Exception ex);

    [LoggerMessage(LogLevel.Information,
        "Reconciled member private channels: {Repaired} repaired, {AccessFixed} permission fixes, {Failed} failed, {CategoriesCreated} categories created.")]
    static partial void LogReconciled(ILogger<ReconcileMemberPrivateChannelsHandler> logger, int repaired, int accessFixed,
        int failed, int categoriesCreated);
}
