using System.Globalization;
using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.MissionBoardNotifications;

/// <summary>
/// Reads each club's mission board and DMs its subscribers when the next board was unlocked or a
/// new week started (#504, <see cref="MissionBoardChangeDetector"/>). Run on every pushed board
/// update and by the safety-net job; the stored observation makes each change go out once.
/// </summary>
/// <param name="ClubId">Only this club, e.g. after its board changed; <c>null</c> for every club.</param>
public sealed record NotifyMissionBoardChangesCommand(Guid? ClubId = null) : ICommand;

public sealed partial class NotifyMissionBoardChangesHandler(
    IClubMissionBoardReader boardReader,
    IServiceScopeFactory scopeFactory,
    IMissionBoardChangeMessages messages,
    IOptions<MissionBoardNotificationsConfiguration> config,
    IOptions<MissionBoardConfiguration> missionBoardConfig,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    TimeProvider timeProvider,
    ILogger<NotifyMissionBoardChangesHandler> logger) : IRequestHandler<NotifyMissionBoardChangesCommand, Unit>
{
    // A pushed update and the scheduled check can look at the same club at once; both would see the
    // same change and DM everyone twice. Static, because handlers are resolved per scope.
    private static readonly KeyedAsyncLock<Guid> ClubLocks = new();

    public async Task<Unit> Handle(NotifyMissionBoardChangesCommand request, CancellationToken cancellationToken)
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
                await CheckClubAsync(clubId, options, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One club failing must not keep the other clubs' subscribers waiting.
                LogClubCheckFailed(ex, clubId);
            }
        }

        return Unit.Value;
    }

    private async Task CheckClubAsync(Guid clubId, MissionBoardNotificationsConfiguration options, CancellationToken cancellationToken)
    {
        using var clubLock = await ClubLocks.AcquireAsync(clubId, cancellationToken).ConfigureAwait(false);

        var week = await boardReader.ReadCurrentAsync(clubId, cancellationToken).ConfigureAwait(false);
        if (week is null)
        {
            return;
        }

        // A scope per club, so a failed save leaves nothing pending for the next one.
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var observations = services.GetRequiredService<IClubMissionBoardObservationRepository>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var now = timeProvider.GetUtcNow();

        var last = await observations.ReadForUpdateAsync(clubId, cancellationToken).ConfigureAwait(false);
        var change = MissionBoardChangeDetector.Detect(last, week);
        if (!change.ShouldStore)
        {
            return;
        }

        if (last is null)
        {
            observations.Add(ClubMissionBoardObservation.Create(clubId, week, now));
        }
        else
        {
            last.Update(week, now);
        }

        // Stored before anyone is told: a crash part-way leaves some without a DM, never all with two.
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (!change.ShouldNotify)
        {
            LogObserved(clubId, change.Kind, week.CurrentBoardNumber);
            return;
        }

        await NotifyAsync(clubId, week, change.Kind, options, services, cancellationToken).ConfigureAwait(false);
    }

    private async Task NotifyAsync(
        Guid clubId,
        ClubMissionBoardWeek week,
        MissionBoardChangeKind kind,
        MissionBoardNotificationsConfiguration options,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var subscriptions = services.GetRequiredService<IMissionBoardSubscriptionRepository>();
        var subscribers = await subscriptions.ReadSubscribersOfClubAsync(clubId, cancellationToken).ConfigureAwait(false);
        if (subscribers.Count == 0)
        {
            LogNoSubscribers(clubId, kind);
            return;
        }

        var clubName = (await services.GetRequiredService<IClubRepository>()
                           .ReadClubByIdAsync(clubId, cancellationToken).ConfigureAwait(false))?.Name
                       ?? clubId.ToString();
        var text = Render(kind == MissionBoardChangeKind.NewWeek ? options.NewWeekMessage : options.BoardUnlockedMessage, week, clubName);
        var cycleStart = week.ClaimCycleStart(timeProvider.GetUtcNow(), missionBoardConfig.Value.FallbackClaimCycleStart);

        int sent = 0, skipped = 0;
        foreach (var subscriber in subscribers)
        {
            // They used this cycle's claim: the free missions are no use to them until it resets.
            if (week.HasClaimedSince(subscriber.GeoGuessrUserId, cycleStart))
            {
                skipped++;
                continue;
            }

            var once = subscriber.Mode == MissionBoardSubscriptionMode.NextChangeOnly;
            var body = once && !string.IsNullOrWhiteSpace(options.NextChangeOnlyFooter)
                ? $"{text}\n-# {options.NextChangeOnlyFooter}"
                : text;

            var result = await messages
                .SendAsync(subscriber.DiscordUserId, body, withUnsubscribeButton: !once, cancellationToken)
                .ConfigureAwait(false);

            if (result.IsSuccess)
            {
                sent++;
                if (once)
                {
                    await subscriptions.DeleteAsync(subscriber.DiscordUserId, cancellationToken).ConfigureAwait(false);
                }
            }
            else if (result.Error.Code == DiscordDmErrorCodes.NoMutualGuild)
            {
                // They left the server: no DM can ever reach them.
                await subscriptions.DeleteAsync(subscriber.DiscordUserId, cancellationToken).ConfigureAwait(false);
                LogSubscriberGone(subscriber.DiscordUserId);
            }
            else
            {
                LogDirectMessageFailed(subscriber.DiscordUserId, result.Error.Code);
            }
        }

        LogNotified(clubId, kind, week.CurrentBoardNumber, sent, skipped);
    }

    public static string Render(string template, ClubMissionBoardWeek week, string clubName) =>
        template
            .Replace("{{club}}", clubName)
            .Replace("{{board}}", week.CurrentBoardNumber.ToString(CultureInfo.InvariantCulture))
            .Replace("{{free}}", week.FreeTiles.Count.ToString(CultureInfo.InvariantCulture))
            .Trim();

    [LoggerMessage(LogLevel.Debug, "Mission board of club {ClubId}: {Kind}, board {Board}; nobody to tell.")]
    partial void LogObserved(Guid clubId, MissionBoardChangeKind kind, int board);

    [LoggerMessage(LogLevel.Debug, "Mission board of club {ClubId}: {Kind}, but nobody is subscribed.")]
    partial void LogNoSubscribers(Guid clubId, MissionBoardChangeKind kind);

    [LoggerMessage(LogLevel.Information,
        "Mission board of club {ClubId}: {Kind}, board {Board}. DMed {Sent} subscribers, skipped {Skipped} who already claimed.")]
    partial void LogNotified(Guid clubId, MissionBoardChangeKind kind, int board, int sent, int skipped);

    [LoggerMessage(LogLevel.Information, "Removed the board subscription of {DiscordUserId}: they left the server.")]
    partial void LogSubscriberGone(ulong discordUserId);

    [LoggerMessage(LogLevel.Warning, "Could not DM user {DiscordUserId} about their club's mission board ({ErrorCode}).")]
    partial void LogDirectMessageFailed(ulong discordUserId, string errorCode);

    [LoggerMessage(LogLevel.Error, "Checking the mission board of club {ClubId} for changes failed.")]
    partial void LogClubCheckFailed(Exception ex, Guid clubId);
}
