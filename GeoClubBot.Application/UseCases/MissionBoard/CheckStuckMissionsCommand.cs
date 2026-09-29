using System.Globalization;
using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;

namespace UseCases.UseCases.MissionBoard;

/// <summary>
/// Alerts about board missions that hold the club up (see <see cref="MissionBoardAlertsConfiguration"/>).
/// Each mission is alerted about at most once per kind, however often this runs.
/// </summary>
public sealed record CheckStuckMissionsCommand : ICommand;

public sealed partial class CheckStuckMissionsHandler(
    IClubMissionBoardReader boardReader,
    IClubRepository clubs,
    IClubMemberRepository clubMembers,
    IMissionBoardAlertRepository alerts,
    IUnitOfWork unitOfWork,
    IDiscordMessageAccess discordMessageAccess,
    IDiscordDirectMessageAccess directMessageAccess,
    IOptions<MissionBoardAlertsConfiguration> config,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    ILogger<CheckStuckMissionsHandler> logger) : IRequestHandler<CheckStuckMissionsCommand, Unit>
{
    /// <summary>Alerts outlive their board week by a week, then they are only clutter.</summary>
    private static readonly TimeSpan AlertRetention = TimeSpan.FromDays(14);

    public async Task<Unit> Handle(CheckStuckMissionsCommand request, CancellationToken cancellationToken)
    {
        var options = config.Value;
        if (!options.Enabled)
        {
            return Unit.Value;
        }

        var now = DateTimeOffset.UtcNow;
        var clubIds = options.ClubIds.Count > 0
            ? options.ClubIds
            : geoGuessrConfig.Value.Clubs.Select(c => c.ClubId).ToList();

        foreach (var clubId in clubIds)
        {
            try
            {
                await CheckClubAsync(clubId, now, options, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One club failing must not keep the other clubs' alerts back.
                LogClubCheckFailed(ex, clubId);
            }
        }

        await alerts.DeleteSentBeforeAsync(now - AlertRetention, cancellationToken).ConfigureAwait(false);

        return Unit.Value;
    }

    private async Task CheckClubAsync(Guid clubId, DateTimeOffset now, MissionBoardAlertsConfiguration options, CancellationToken cancellationToken)
    {
        var board = await boardReader.ReadCurrentAsync(clubId, cancellationToken).ConfigureAwait(false);
        if (board is null)
        {
            return;
        }

        var stuck = StuckMissionDetector.Find(board, now, options);
        if (stuck.Count == 0)
        {
            return;
        }

        var alreadySent = await alerts
            .ReadSentAlertsAsync(clubId, stuck.Select(s => s.Tile.MissionId).Distinct().ToList(), cancellationToken)
            .ConfigureAwait(false);
        var toSend = stuck.Where(s => !alreadySent.Contains((s.Tile.MissionId, s.Kind))).ToList();
        if (toSend.Count == 0)
        {
            return;
        }

        var clubName = (await clubs.ReadClubByIdAsync(clubId, cancellationToken).ConfigureAwait(false))?.Name
                       ?? clubId.ToString();
        var claimers = await clubMembers
            .ReadClubMembersByUserIdsAsync(toSend.Select(s => s.Tile.ClaimedBy!).Distinct().ToList(), cancellationToken)
            .ConfigureAwait(false);

        foreach (var (tile, kind) in toSend)
        {
            var claimer = claimers.GetValueOrDefault(tile.ClaimedBy!);
            var discordUserId = claimer?.User.DiscordUserId;
            var nickname = claimer?.User.Nickname ?? tile.ClaimedBy!;
            var mention = options.MentionClaimer && discordUserId is { } id ? $"<@{id}>" : $"**{nickname}**";

            var template = kind == MissionBoardAlertKind.HelpRequest ? options.HelpRequestMessage : options.OpenClaimMessage;

            if (options.TextChannelId is { } channelId)
            {
                var mentions = options.MentionClaimer && discordUserId is { } userId
                    ? new MessageMentions([], [userId], false)
                    : MessageMentions.None;

                await discordMessageAccess
                    .SendMessageAsync(Render(template, tile, board, mention, clubName), channelId, mentions, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (options.DmClaimer && kind == MissionBoardAlertKind.OpenClaim && discordUserId is { } dmUserId)
            {
                var dm = await directMessageAccess
                    .SendDirectMessageAsync(dmUserId, Render(options.OpenClaimDirectMessage, tile, board, nickname, clubName), cancellationToken)
                    .ConfigureAwait(false);
                if (dm.IsFailure)
                {
                    LogDirectMessageFailed(dmUserId, dm.Error.Code);
                }
            }

            // Recorded one by one, so an alert that went out is never repeated even if a later one
            // in this run fails.
            alerts.Add(MissionBoardAlert.Create(clubId, tile.MissionId, kind, DateTimeOffset.UtcNow));
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            LogAlertSent(kind, tile.MissionId, clubId);
        }
    }

    public static string Render(string template, ClubMissionTile tile, ClubMissionBoardWeek board, string claimer, string clubName) =>
        template
            .Replace("{{claimer}}", claimer)
            .Replace("{{mission}}", tile.Title)
            .Replace("{{progress}}", $"{tile.CurrentProgress.ToString(CultureInfo.InvariantCulture)}/{tile.TargetProgress.ToString(CultureInfo.InvariantCulture)}")
            .Replace("{{claimed_at}}", tile.ClaimedAt is { } at ? $"<t:{at.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}:R>" : "a while ago")
            .Replace("{{board}}", tile.BoardNumber.ToString(CultureInfo.InvariantCulture))
            .Replace("{{remaining}}", (board.Boards.FirstOrDefault(b => b.Number == tile.BoardNumber)?.RemainingCount ?? 0).ToString(CultureInfo.InvariantCulture))
            .Replace("{{club}}", clubName);

    [LoggerMessage(LogLevel.Information, "Sent a {Kind} alert for mission {MissionId} of club {ClubId}.")]
    partial void LogAlertSent(MissionBoardAlertKind kind, Guid missionId, Guid clubId);

    [LoggerMessage(LogLevel.Warning, "Could not DM user {DiscordUserId} about their stuck mission ({ErrorCode}).")]
    partial void LogDirectMessageFailed(ulong discordUserId, string errorCode);

    [LoggerMessage(LogLevel.Error, "Checking the mission board of club {ClubId} for stuck missions failed.")]
    partial void LogClubCheckFailed(Exception ex, Guid clubId);
}
