using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.ClubChatForwarding;

/// <summary>
/// Sends <paramref name="Text"/> into the chats of <paramref name="ClubIds"/>, split into parts
/// GeoGuessr won't cut, and records each club's forward of <paramref name="DiscordMessageId"/>.
/// </summary>
public sealed record ForwardToClubChatCommand(
    ulong DiscordMessageId,
    ulong ActorDiscordUserId,
    string Text,
    IReadOnlyList<Guid> ClubIds) : ICommand<Result<ClubChatForwardReport>>;

public sealed record ClubChatForwardResult(ClubChatTarget Club, ClubChatDelivery Delivery);

public sealed record ClubChatForwardReport(int PartCount, IReadOnlyList<ClubChatForwardResult> Clubs);

public sealed partial class ForwardToClubChatHandler(
    ClubChatForwardingAccess access,
    IGeoGuessrClubChatSender sender,
    IClubChatForwardRepository forwards,
    IOptions<ClubChatForwardingConfiguration> config,
    TimeProvider timeProvider,
    ILogger<ForwardToClubChatHandler> logger) : IRequestHandler<ForwardToClubChatCommand, Result<ClubChatForwardReport>>
{
    public async Task<Result<ClubChatForwardReport>> Handle(ForwardToClubChatCommand request, CancellationToken cancellationToken)
    {
        var allowed = await access.CheckAsync(request.ActorDiscordUserId, cancellationToken).ConfigureAwait(false);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var options = config.Value;
        var split = ClubChatTextSplitter.Split(request.Text, options.MaxMessageLength, options.MaxParts);
        if (split.IsFailure)
        {
            return split.Error;
        }

        var targets = await access.ReadTargetsAsync(cancellationToken).ConfigureAwait(false);
        var chosen = targets.Where(t => request.ClubIds.Contains(t.ClubId)).ToList();
        if (chosen.Count == 0)
        {
            return Error.Validation("club-chat.no_club", "Pick at least one club.");
        }

        var parts = split.Value;

        // Clubs in parallel (each has its own account and socket), parts of one club in order.
        var deliveries = await Task.WhenAll(chosen.Select(club => DeliverAsync(club, parts, options.EchoTimeout, cancellationToken)))
            .ConfigureAwait(false);

        var forwardedAt = timeProvider.GetUtcNow();
        foreach (var result in deliveries)
        {
            // Recorded whatever the outcome once anything may have arrived, so a retry gets warned about.
            if (result.Delivery.PartsSent > 0)
            {
                forwards.Add(ClubChatForward.Create(
                    request.DiscordMessageId, result.Club.ClubId, request.ActorDiscordUserId, forwardedAt,
                    parts.Count, result.Delivery.PartsSent, result.Delivery.PartsConfirmed));
            }

            LogForwarded(result.Club.ClubId, request.DiscordMessageId, request.ActorDiscordUserId, result.Delivery.Status,
                result.Delivery.PartsConfirmed, parts.Count, result.Delivery.Detail);
        }

        return new ClubChatForwardReport(parts.Count, deliveries);
    }

    private async Task<ClubChatForwardResult> DeliverAsync(
        ClubChatTarget club, IReadOnlyList<string> parts, TimeSpan echoTimeout, CancellationToken cancellationToken)
    {
        try
        {
            var delivery = await sender.SendAsync(club.ClubId, parts, echoTimeout, cancellationToken).ConfigureAwait(false);
            return new ClubChatForwardResult(club, delivery);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The sender reports its failures; this is only so one club can't lose the others' report.
            LogSendFailed(ex, club.ClubId);
            return new ClubChatForwardResult(club, new ClubChatDelivery(ClubChatDeliveryStatus.NotConnected, 0, 0, "unexpected error"));
        }
    }

    [LoggerMessage(LogLevel.Information,
        "Forwarded Discord message {MessageId} by {ActorId} to the chat of club {ClubId}: {Status}, {Confirmed}/{Parts} parts confirmed ({Detail}).")]
    partial void LogForwarded(Guid clubId, ulong messageId, ulong actorId, ClubChatDeliveryStatus status, int confirmed, int parts, string? detail);

    [LoggerMessage(LogLevel.Error, "Forwarding to the chat of club {ClubId} failed unexpectedly.")]
    partial void LogSendFailed(Exception ex, Guid clubId);
}
