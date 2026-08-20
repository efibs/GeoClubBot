using Configuration;
using Entities;

namespace UseCases.UseCases.AI.Conversations;

/// <param name="ImageUrls">Already filtered to the images that fit the budget.</param>
public sealed record ConversationTurnView(
    AiTurnRole Role,
    ulong AuthorDiscordUserId,
    string Content,
    IReadOnlyList<string> ImageUrls);

/// <param name="WasTrimmed">True when history was dropped, so the model can be told the thread is partial.</param>
/// <param name="ParentDepth">Depth of the turn being replied to; the new turn sits one below.</param>
public sealed record ConversationContext(
    IReadOnlyList<ConversationTurnView> Turns,
    bool WasTrimmed,
    int ParentDepth)
{
    public static readonly ConversationContext Empty = new([], false, -1);

    public bool IsNewConversation => Turns.Count == 0;
}

/// <summary>
/// Turns a stored conversation tree into the linear history replayed to the model.
///
/// The context for a message is the path from it up to the root — not everything in the channel, and
/// not everything in the conversation. That is what keeps sibling branches independent when several
/// people reply to the same answer: each sees the shared prefix and its own replies, never the others'.
///
/// Pure by design so every limit can be tested without a database or a Discord connection.
/// </summary>
public static class ConversationContextBuilder
{
    /// <summary>
    /// Guards against a malformed tree (a parent cycle) turning the walk into an infinite loop.
    /// Generous relative to any sane MaxTurns, because trimming happens afterwards.
    /// </summary>
    private const int MaxWalkSteps = 500;

    /// <summary>
    /// Builds the history to replay for a reply to <paramref name="parentMessageId"/>.
    /// Returns <see cref="ConversationContext.Empty"/> when the chain is unknown or has gone stale,
    /// which the caller treats as the start of a fresh conversation.
    /// </summary>
    /// <param name="conversationTurns">Every stored turn of the conversation, in any order.</param>
    public static ConversationContext Build(
        IReadOnlyCollection<AiConversationTurn> conversationTurns,
        ulong parentMessageId,
        AiConversationConfiguration limits,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(conversationTurns);
        ArgumentNullException.ThrowIfNull(limits);

        if (conversationTurns.Count == 0)
        {
            return ConversationContext.Empty;
        }

        var byMessageId = conversationTurns
            .GroupBy(turn => turn.DiscordMessageId)
            .ToDictionary(group => group.Key, group => group.First());

        if (!byMessageId.TryGetValue(parentMessageId, out var parent))
        {
            // The parent is not ours or has aged out of retention. Answering fresh is friendlier than
            // refusing, and the user's own message still carries whatever they quoted.
            return ConversationContext.Empty;
        }

        // Idle time is measured from the branch's most recent turn rather than from the root, so an
        // actively running thread is never cut off mid-discussion just because it started yesterday.
        if (nowUtc - parent.CreatedAtUtc > TimeSpan.FromHours(limits.MaxIdleHours))
        {
            return ConversationContext.Empty;
        }

        var path = WalkToRoot(byMessageId, parent);
        var (trimmed, wasTrimmed) = ApplyLimits(path, limits);

        return new ConversationContext(BuildViews(trimmed, limits.MaxImagesInContext), wasTrimmed, parent.Depth);
    }

    /// <summary>Collects the ancestor path, oldest first.</summary>
    private static List<AiConversationTurn> WalkToRoot(
        Dictionary<ulong, AiConversationTurn> byMessageId,
        AiConversationTurn leaf)
    {
        var path = new List<AiConversationTurn>();
        var visited = new HashSet<ulong>();
        var current = leaf;

        for (var step = 0; step < MaxWalkSteps; step++)
        {
            // A repeated id means the stored tree has a cycle; stop rather than loop forever.
            if (!visited.Add(current.DiscordMessageId))
            {
                break;
            }

            path.Add(current);

            if (current.ParentDiscordMessageId is not { } parentId
                || !byMessageId.TryGetValue(parentId, out var parent))
            {
                // Root reached, or a link into turns we no longer store. Either way the walk is done;
                // a partial prefix is still useful history.
                break;
            }

            current = parent;
        }

        path.Reverse();
        return path;
    }

    /// <summary>Drops the oldest turns until the path fits both the turn count and character budget.</summary>
    private static (List<AiConversationTurn> Turns, bool WasTrimmed) ApplyLimits(
        List<AiConversationTurn> path,
        AiConversationConfiguration limits)
    {
        var wasTrimmed = false;

        var maxTurns = Math.Max(1, limits.MaxTurns);
        if (path.Count > maxTurns)
        {
            path = path[^maxTurns..];
            wasTrimmed = true;
        }

        // Trim from the oldest end only. Removing from the middle would leave a hole in the reply
        // chain and produce incoherent history.
        var budget = Math.Max(0, limits.MaxContextCharacters);
        var total = path.Sum(turn => turn.Content.Length);
        while (path.Count > 1 && total > budget)
        {
            total -= path[0].Content.Length;
            path.RemoveAt(0);
            wasTrimmed = true;
        }

        return (path, wasTrimmed);
    }

    /// <summary>
    /// Projects to views, keeping only the newest images. Older turns keep their text and lose their
    /// pictures, which is the cheapest way to stay inside a small context window.
    /// </summary>
    private static List<ConversationTurnView> BuildViews(List<AiConversationTurn> path, int maxImages)
    {
        var remainingImages = Math.Max(0, maxImages);
        var views = new ConversationTurnView[path.Count];

        // Walk newest first so the images that survive are the most recent ones.
        for (var index = path.Count - 1; index >= 0; index--)
        {
            var turn = path[index];
            var images = new List<string>();

            foreach (var url in turn.ImageUrls)
            {
                if (remainingImages == 0)
                {
                    break;
                }

                images.Add(url);
                remainingImages--;
            }

            views[index] = new ConversationTurnView(turn.Role, turn.AuthorDiscordUserId, turn.Content, images);
        }

        return [.. views];
    }
}
