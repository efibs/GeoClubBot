using MediatR;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;

namespace UseCases.UseCases.AI.Conversations;

/// <summary>
/// Whether a Discord message is a turn the bot already stored.
///
/// Used by the gateway to recognise a reply as a continuation without requiring the user to mention
/// the bot again. Discord.Net's cached <c>ReferencedMessage</c> answers this for recent messages but
/// is empty for older ones, and falling back to "did they ping us" would silently break follow-ups on
/// anything that has scrolled out of cache.
/// </summary>
public sealed record IsKnownAiTurnQuery(ulong DiscordMessageId) : IQuery<bool>;

public sealed class IsKnownAiTurnHandler(IAiConversationRepository conversations)
    : IRequestHandler<IsKnownAiTurnQuery, bool>
{
    public async Task<bool> Handle(IsKnownAiTurnQuery request, CancellationToken cancellationToken) =>
        await conversations.ReadByMessageIdAsync(request.DiscordMessageId, cancellationToken)
            .ConfigureAwait(false) is not null;
}
