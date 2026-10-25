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
using UseCases.UseCases.CountryChallenges.Rendering;

namespace UseCases.UseCases.ClubApplications;

/// <summary>
/// Announces new applications to the clubs (see <see cref="ClubApplicationNotificationsConfiguration"/>).
/// Each pending application is announced once, however often this runs.
/// </summary>
public sealed record CheckClubApplicationsCommand : ICommand;

public sealed partial class CheckClubApplicationsHandler(
    IGeoGuessrClientFactory clientFactory,
    IClubRepository clubs,
    IGeoGuessrUserRepository users,
    IClubApplicationNotificationRepository notifications,
    IUnitOfWork unitOfWork,
    IDiscordMessageAccess discordMessageAccess,
    IOptions<ClubApplicationNotificationsConfiguration> config,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    ILogger<CheckClubApplicationsHandler> logger) : IRequestHandler<CheckClubApplicationsCommand, Unit>
{
    /// <summary>A club holds a few dozen applications at most; this only stops a token that never ends.</summary>
    private const int MaxPages = 20;

    public async Task<Unit> Handle(CheckClubApplicationsCommand request, CancellationToken cancellationToken)
    {
        var options = config.Value;
        if (!options.Enabled || options.TextChannelId is not { } channelId)
        {
            return Unit.Value;
        }

        var clubIds = geoGuessrConfig.Value.Clubs
            .Where(c => c.NotifyApplications != false)
            .Select(c => c.ClubId)
            .ToList();

        foreach (var clubId in clubIds)
        {
            try
            {
                await CheckClubAsync(clubId, channelId, options, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One club failing must not keep the other clubs' notifications back.
                LogClubCheckFailed(ex, clubId);
            }
        }

        return Unit.Value;
    }

    private async Task CheckClubAsync(Guid clubId, ulong channelId, ClubApplicationNotificationsConfiguration options, CancellationToken cancellationToken)
    {
        // Read in full before anything is pruned: a half-read list would forget announcements of
        // applications that are still pending and announce them again.
        var pending = await ReadPendingAsync(clubId, cancellationToken).ConfigureAwait(false);
        var notified = await notifications.ReadNotifiedUserIdsAsync(clubId, cancellationToken).ConfigureAwait(false);

        var toAnnounce = pending.Where(a => !notified.Contains(a.UserId)).ToList();
        if (toAnnounce.Count > 0)
        {
            var clubName = (await clubs.ReadClubByIdAsync(clubId, cancellationToken).ConfigureAwait(false))?.Name
                           ?? clubId.ToString();
            var mentions = options.RoleIdToPing is { } roleId
                ? new MessageMentions([roleId], [], false)
                : MessageMentions.None;

            foreach (var applicant in toAnnounce)
            {
                var linked = await users.ReadUserByUserIdAsync(applicant.UserId, cancellationToken).ConfigureAwait(false);
                var message = Render(options.Message, applicant, linked?.DiscordUserId, clubName, options.RoleIdToPing);

                await discordMessageAccess.SendMessageAsync(message, channelId, mentions, cancellationToken).ConfigureAwait(false);

                // Recorded one by one, so an announcement that went out is never repeated even if a
                // later one in this run fails.
                notifications.Add(ClubApplicationNotification.Create(clubId, applicant.UserId, DateTimeOffset.UtcNow));
                await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                LogApplicationAnnounced(applicant.UserId, clubId);
            }
        }

        // Accepted, rejected or withdrawn: forget it, so a re-application is announced again.
        var pendingIds = pending.Select(a => a.UserId).ToHashSet();
        var gone = notified.Where(id => !pendingIds.Contains(id)).ToList();
        if (gone.Count > 0)
        {
            await notifications.DeleteAsync(clubId, gone, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<List<ClubMemberUserDto>> ReadPendingAsync(Guid clubId, CancellationToken cancellationToken)
    {
        // The applications are only visible to staff of the club, so read them with its own token.
        var client = clientFactory.CreateClient(clubId);
        var pending = new List<ClubMemberUserDto>();
        string? paginationToken = null;

        for (var page = 0; page < MaxPages; page++)
        {
            var response = await client.ReadClubApplicationsAsync(clubId, paginationToken, cancellationToken).ConfigureAwait(false);
            pending.AddRange(response.Items);

            paginationToken = response.PaginationToken;
            if (string.IsNullOrEmpty(paginationToken))
            {
                return pending.DistinctBy(a => a.UserId).ToList();
            }
        }

        throw new InvalidOperationException($"The applications of club {clubId} did not end after {MaxPages} pages.");
    }

    public static string Render(string template, ClubMemberUserDto applicant, ulong? discordUserId, string clubName, ulong? roleId)
    {
        var nick = CountryChallengeMessages.EscapeMarkdown(applicant.Nick);
        var discord = discordUserId is { } id ? $"<@{id.ToString(CultureInfo.InvariantCulture)}>" : string.Empty;

        return template
            .Replace("{{club}}", CountryChallengeMessages.EscapeMarkdown(clubName))
            .Replace("{{applicant}}", discordUserId is null ? $"**{nick}**" : $"**{nick}** ({discord})")
            .Replace("{{nick}}", nick)
            .Replace("{{discord}}", discord)
            .Replace("{{profile}}", $"https://www.geoguessr.com/user/{Uri.EscapeDataString(applicant.UserId)}")
            .Replace("{{role}}", roleId is { } role ? $"<@&{role.ToString(CultureInfo.InvariantCulture)}>" : string.Empty)
            .Trim();
    }

    [LoggerMessage(LogLevel.Information, "Announced the application of user {UserId} to club {ClubId}.")]
    partial void LogApplicationAnnounced(string userId, Guid clubId);

    [LoggerMessage(LogLevel.Error, "Checking the applications of club {ClubId} failed.")]
    partial void LogClubCheckFailed(Exception ex, Guid clubId);
}
