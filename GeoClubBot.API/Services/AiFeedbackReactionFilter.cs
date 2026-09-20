using Configuration;
using Entities;

namespace GeoClubBot.Services;

/// <summary>
/// The parts of a reaction the filter needs.
///
/// Projected off Discord's own type rather than passed straight through: SocketReaction cannot be
/// constructed outside Discord.Net, so taking it here would make the whole decision untestable.
/// </summary>
/// <param name="UserIsBot">
/// Only meaningful when Discord supplied the user, which it often does not for an uncached reaction.
/// The id comparison against the bot itself is the reliable check; this is extra cover.
/// </param>
public sealed record ReactionSignal(
    string EmoteName,
    ulong UserId,
    bool UserIsBot,
    bool IsGuildChannel,
    ulong ChannelId);

/// <summary>
/// Decides whether a reaction is a verdict on an AI answer, before anything touches the database.
///
/// Every reaction in every channel the bot can see arrives at the listener, so the order here is
/// deliberate: the emoji check alone discards nearly all of them, and nothing below reaches I/O.
/// </summary>
public static class AiFeedbackReactionFilter
{
    /// <summary>
    /// The verdict a reaction represents, or null when it is not one this feature cares about.
    /// </summary>
    public static AiFeedbackRating? Classify(
        ReactionSignal reaction,
        ulong botUserId,
        AiConfiguration aiConfiguration,
        AiFeedbackConfiguration feedbackConfiguration)
    {
        ArgumentNullException.ThrowIfNull(reaction);
        ArgumentNullException.ThrowIfNull(aiConfiguration);
        ArgumentNullException.ThrowIfNull(feedbackConfiguration);

        if (!feedbackConfiguration.Enabled)
        {
            return null;
        }

        // Cheapest discriminator first, and the one that rejects almost everything.
        var rating = RatingFor(reaction.EmoteName, feedbackConfiguration);
        if (rating is null)
        {
            return null;
        }

        // Our own prefilled reactions raise this event too, so with PrefillReactions on this is the
        // common case rather than an edge one.
        if (reaction.UserId == botUserId || reaction.UserIsBot)
        {
            return null;
        }

        // Guild-only, mirroring the answering side: a DM bypasses the channel allowlist entirely.
        if (!reaction.IsGuildChannel)
        {
            return null;
        }

        var allowedChannels = aiConfiguration.AllowedChannelIds;
        if (allowedChannels.Count > 0 && !allowedChannels.Contains(reaction.ChannelId))
        {
            return null;
        }

        return rating;
    }

    /// <summary>
    /// Matched on the emoji's name, which for a standard emoji is its codepoint. A custom guild
    /// emote would match on its alias instead, which is why configuration takes unicode only.
    /// </summary>
    private static AiFeedbackRating? RatingFor(string emoteName, AiFeedbackConfiguration configuration)
    {
        if (string.Equals(emoteName, configuration.PositiveEmoji, StringComparison.Ordinal))
        {
            return AiFeedbackRating.Positive;
        }

        return string.Equals(emoteName, configuration.NegativeEmoji, StringComparison.Ordinal)
            ? AiFeedbackRating.Negative
            : null;
    }
}
