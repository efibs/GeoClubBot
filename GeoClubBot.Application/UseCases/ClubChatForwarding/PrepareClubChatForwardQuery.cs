using Configuration;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.ClubChatForwarding;

/// <summary>
/// Everything the forwarding form shows for a Discord message: the text it is prefilled with, the
/// clubs that can receive it, and earlier forwards of the same message.
/// </summary>
/// <param name="FlattenedText">The message as plain text (see <see cref="DiscordTextFlattener"/>).</param>
/// <param name="Author">The message author's server display name.</param>
/// <param name="Channel">The name of the channel it was posted in.</param>
public sealed record PrepareClubChatForwardQuery(
    ulong DiscordMessageId,
    ulong ActorDiscordUserId,
    string FlattenedText,
    string Author,
    string Channel) : IQuery<Result<ClubChatForwardDraft>>;

public sealed record ClubChatTarget(Guid ClubId, string Name);

public sealed record EarlierClubChatForward(string ClubName, ulong ForwardedByDiscordUserId, DateTimeOffset ForwardedAt, int PartCount, int PartsConfirmed);

/// <param name="PartCount">How many chat messages <paramref name="Text"/> needs, or null when it needs too many.</param>
public sealed record ClubChatForwardDraft(
    string Text,
    int? PartCount,
    IReadOnlyList<ClubChatTarget> Clubs,
    IReadOnlyList<EarlierClubChatForward> EarlierForwards);

public sealed class PrepareClubChatForwardHandler(
    ClubChatForwardingAccess access,
    IClubChatForwardRepository forwards,
    IOptions<ClubChatForwardingConfiguration> config) : IRequestHandler<PrepareClubChatForwardQuery, Result<ClubChatForwardDraft>>
{
    public async Task<Result<ClubChatForwardDraft>> Handle(PrepareClubChatForwardQuery request, CancellationToken cancellationToken)
    {
        var allowed = await access.CheckAsync(request.ActorDiscordUserId, cancellationToken).ConfigureAwait(false);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var options = config.Value;
        var text = ClubChatTemplate.Render(options.Template, request.FlattenedText, request.Author, request.Channel);
        var split = ClubChatTextSplitter.Split(text, options.MaxMessageLength, options.MaxParts);

        var clubs = await access.ReadTargetsAsync(cancellationToken).ConfigureAwait(false);
        var names = clubs.ToDictionary(c => c.ClubId, c => c.Name);
        var earlier = await forwards.ReadByMessageAsync(request.DiscordMessageId, cancellationToken).ConfigureAwait(false);

        return new ClubChatForwardDraft(
            text,
            split.IsSuccess ? split.Value.Count : null,
            clubs,
            earlier
                .Select(f => new EarlierClubChatForward(
                    names.GetValueOrDefault(f.ClubId, f.ClubId.ToString()), f.ForwardedByDiscordUserId, f.ForwardedAt, f.PartCount, f.PartsConfirmed))
                .ToList());
    }
}
