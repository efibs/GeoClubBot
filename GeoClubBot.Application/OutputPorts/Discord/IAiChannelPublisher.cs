using UseCases.UseCases.AI.Conversations;

namespace UseCases.OutputPorts.Discord;

/// <summary>
/// Posts something shaped like an AI answer — text, pictures uploaded from the relay, a source list —
/// into a channel of its own, rather than as a reply to a question. What the clue of the day uses.
/// </summary>
public interface IAiChannelPublisher
{
    /// <summary>The id of the last message posted, or null when the channel could not be posted to.</summary>
    Task<ulong?> PostAsync(ulong channelId, AiAnswer answer, CancellationToken cancellationToken = default);
}
