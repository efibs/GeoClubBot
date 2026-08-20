using Discord;
using Extensions;
using UseCases.UseCases.AI.Conversations;

namespace GeoClubBot.Discord.InputAdapters.Interactions.AI;

/// <param name="MessageChunks">Answer split to Discord's per-message limit; post in order.</param>
public sealed record AiAnswerRendering(IReadOnlyList<string> MessageChunks, IReadOnlyList<Embed> Embeds);

/// <summary>
/// Turns an answer into the messages and embeds Discord will actually show.
///
/// Pure, so the layout is snapshot-tested rather than eyeballed in a live server.
/// </summary>
public static class AiAnswerFormatter
{
    private const int DiscordMessageLimit = 2000;

    /// <summary>Discord renders at most ten embeds per message.</summary>
    private const int MaxEmbeds = 10;

    public static AiAnswerRendering Render(AiAnswer answer)
    {
        var body = answer.Text.Trim();
        if (body.Length == 0)
        {
            body = "_(the model returned nothing to show)_";
        }

        var footer = BuildFooter(answer);
        var full = footer.Length == 0 ? body : $"{body}\n\n{footer}";

        var chunks = full.SplitAtCharWithLimit("\n", DiscordMessageLimit).ToList();

        var embeds = answer.Images
            .Take(MaxEmbeds)
            .Select(image => new EmbedBuilder()
                .WithImageUrl(image.ImageUrl)
                // Linking the embed to its source makes every shown image a citation rather than a
                // decoration, and credits the guide it came from.
                .WithUrl(image.SourceUrl)
                .WithTitle(string.IsNullOrWhiteSpace(image.Title) ? "Guide image" : image.Title)
                .Build())
            .ToList();

        return new AiAnswerRendering(chunks, embeds);
    }

    /// <summary>
    /// Subtext credits the model that answered, so a drop in quality can be traced to a specific one
    /// rather than blamed on "the bot".
    /// </summary>
    private static string BuildFooter(AiAnswer answer)
    {
        var lines = new List<string>();

        if (answer.IsLongThread)
        {
            lines.Add("-# This thread is getting long — mention me in a new message to start fresh.");
        }

        if (!string.IsNullOrWhiteSpace(answer.ModelUsed))
        {
            lines.Add($"-# via {answer.ModelUsed}");
        }

        return string.Join("\n", lines);
    }
}
