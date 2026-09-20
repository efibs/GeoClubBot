using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Discord;
using Entities;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Feedback;

namespace GeoClubBot.Discord.InputAdapters.Interactions.AI;

/// <summary>
/// Renders the archive for human eyes (a summary embed) and for machines (JSONL).
///
/// Pure and static, like <see cref="AiAnswerFormatter"/>, so the whole layout can be snapshot-tested
/// without a Discord connection.
/// </summary>
public static class AiFeedbackFormatter
{
    /// <summary>Comment length in the summary. Enough to recognise one; the export carries the rest.</summary>
    private const int CommentPreviewLength = 120;

    /// <summary>Models listed in the summary, worst first.</summary>
    private const int ModelBreakdownLimit = 5;

    private static readonly JsonSerializerOptions ExportOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

        // Escaped conservatively by default, which would turn every accented place name in a guide
        // answer into a \uXXXX escape and make the export unreadable by eye.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// One JSON object per line — the shape evaluation tooling reads, and the shape that lets a large
    /// archive be processed a record at a time.
    /// </summary>
    public static string RenderJsonLines(IReadOnlyList<AiFeedbackRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var builder = new StringBuilder();

        foreach (var record in records)
        {
            builder.Append(JsonSerializer.Serialize(record, ExportOptions)).Append('\n');
        }

        return builder.ToString();
    }

    public static Embed BuildSummaryEmbed(AiFeedbackCounts counts, int? days)
    {
        ArgumentNullException.ThrowIfNull(counts);

        var window = days is > 0 ? $"last {days} day(s)" : "all time";

        var embed = new EmbedBuilder()
            .WithTitle("🗳️ AI answer feedback")
            .WithDescription($"Window: {window}")
            .AddField("👍 Good", counts.Positive, inline: true)
            .AddField("👎 Bad", counts.Negative, inline: true)
            .AddField("With a comment", counts.WithComment, inline: true);

        if (counts.Total == 0)
        {
            embed.AddField("Nothing rated yet",
                "React 👍 or 👎 on one of my answers, or right-click it → Apps → **👎 Bad AI answer** "
                + "to add a comment.");

            return embed.Build();
        }

        if (counts.ByModel.Count > 0)
        {
            embed.AddField("By model", string.Join("\n", counts.ByModel
                .Take(ModelBreakdownLimit)
                .Select(model => $"`{model.ModelId}` — 👍 {model.Positive} · 👎 {model.Negative}")));
        }

        if (counts.RecentComments.Count > 0)
        {
            embed.AddField("Recent comments", string.Join("\n", counts.RecentComments.Select(comment =>
                $"{(comment.Rating == AiFeedbackRating.Positive ? "👍" : "👎")} "
                + $"{TimestampTag.FromDateTimeOffset(comment.CreatedAtUtc, TimestampTagStyles.Relative)} "
                + $"— {Preview(comment.Comment)}")));
        }

        return embed.Build();
    }

    /// <summary>Collapsed to one line: a multi-line comment would break the embed's field layout.</summary>
    private static string Preview(string comment)
    {
        var flattened = comment.ReplaceLineEndings(" ").Trim();

        return flattened.Length <= CommentPreviewLength
            ? flattened
            : flattened[..CommentPreviewLength] + "…";
    }
}
