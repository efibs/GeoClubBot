using System.Text;
using System.Text.Json;
using Discord;
using Entities;
using FluentAssertions;
using GeoClubBot.Discord.InputAdapters.Interactions.AI;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Feedback;
using Xunit;
using static VerifyXunit.Verifier;

namespace GeoClubBot.Tests.Discord;

/// <summary>
/// The export is the whole point of collecting feedback, so its shape is pinned rather than left to
/// drift: it is read by tooling, and a silently renamed field breaks that without breaking a build.
/// The summary embed is snapshot-tested for the same reason the other formatters are.
/// </summary>
public sealed class AiFeedbackFormatterTests
{
    // Fixed so the <t:...:R> timestamps in the snapshots stay deterministic.
    private static readonly DateTimeOffset Rated = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RenderJsonLines_WritesOneObjectPerRecord()
    {
        var jsonl = AiFeedbackFormatter.RenderJsonLines([Record("bad"), Record("worse")]);

        var lines = jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(2);
        lines.Should().OnlyContain(line => line.StartsWith('{') && line.EndsWith('}'));
    }

    [Fact]
    public void RenderJsonLines_CarriesTheTranscriptAndTheRetrievalTrace()
    {
        // Both halves of the diagnosis: what was said, and which guides were on the table when it
        // was said. Without the second, a bad answer cannot be told from a bad retrieval.
        var line = AiFeedbackFormatter.RenderJsonLines([Record("bad")]).TrimEnd('\n');

        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;

        root.GetProperty("rating").GetString().Should().Be("negative");
        root.GetProperty("comment").GetString().Should().Be("bad");
        root.GetProperty("modelId").GetString().Should().Be("test/model");

        var transcript = root.GetProperty("transcript");
        transcript.GetArrayLength().Should().Be(2);
        transcript[0].GetProperty("role").GetString().Should().Be("user");
        transcript[1].GetProperty("role").GetString().Should().Be("assistant");

        root.GetProperty("retrievedSourceUrls").GetArrayLength().Should().Be(2);
        root.GetProperty("citedSourceUrls").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void RenderJsonLines_LeavesNonAsciiTextReadable()
    {
        // Guide answers are full of accented place names; the default encoder would turn every one
        // of them into a \uXXXX escape and make the file unreadable by eye.
        var line = AiFeedbackFormatter.RenderJsonLines([Record("Côte d'Ivoire, not Ghana")]);

        line.Should().Contain("Côte d'Ivoire");
    }

    [Fact]
    public void RenderJsonLines_ProducesNothingForAnEmptyArchive()
    {
        AiFeedbackFormatter.RenderJsonLines([]).Should().BeEmpty();
    }

    [Fact]
    public Task BuildSummaryEmbed_BreaksTheCountsDownByModelAndShowsRecentComments()
    {
        var counts = new AiFeedbackCounts(
            Positive: 12,
            Negative: 5,
            WithComment: 3,
            ByModel:
            [
                new AiFeedbackModelCount("google/gemma-4-31b-it:free", 3, 4),
                new AiFeedbackModelCount("test/model", 9, 1)
            ],
            RecentComments:
            [
                new AiFeedbackCommentPreview(
                    AiFeedbackRating.Negative, "Said Ghana but the bollards are Togolese.", "google/gemma-4-31b-it:free", Rated),
                new AiFeedbackCommentPreview(
                    AiFeedbackRating.Positive, "Spot on, and the guide link was the right one.", "test/model", Rated)
            ]);

        return Verify(RenderEmbed(AiFeedbackFormatter.BuildSummaryEmbed(counts, days: 30)));
    }

    [Fact]
    public Task BuildSummaryEmbed_ExplainsHowToRate_WhenNothingHasBeenRatedYet()
    {
        // The first thing an admin sees. Zeroes alone do not say whether the feature is broken or
        // simply unused.
        var counts = new AiFeedbackCounts(0, 0, 0, [], []);

        return Verify(RenderEmbed(AiFeedbackFormatter.BuildSummaryEmbed(counts, days: null)));
    }

    [Fact]
    public void BuildSummaryEmbed_FlattensAMultiLineComment()
    {
        // A raw newline would break the embed's field layout.
        var counts = new AiFeedbackCounts(0, 1, 1, [],
            [new AiFeedbackCommentPreview(AiFeedbackRating.Negative, "first line\nsecond line", null, Rated)]);

        var embed = AiFeedbackFormatter.BuildSummaryEmbed(counts, days: 7);

        embed.Fields.Single(field => field.Name == "Recent comments").Value.Should().NotContain("\n");
    }

    private static AiFeedbackRecord Record(string comment) =>
        new(
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            Rating: "negative",
            Comment: comment,
            ReviewerDiscordUserId: 42,
            RatedDiscordMessageId: 101,
            ConversationId: 100,
            ChannelId: 5,
            GuildId: 7,
            ModelId: "test/model",
            AnswerDepth: 1,
            CreatedAtUtc: Rated,
            UpdatedAtUtc: Rated,
            Transcript:
            [
                new AiFeedbackTurnRecord(0, "user", 42, "what country?", [], null, Rated),
                new AiFeedbackTurnRecord(1, "assistant", 1, "Ghana.", [], "test/model", Rated)
            ],
            RetrievedSourceUrls: ["https://plonkit.net/ghana", "https://plonkit.net/togo"],
            CitedSourceUrls: ["https://plonkit.net/ghana"]);

    private static string RenderEmbed(Embed embed)
    {
        var text = new StringBuilder()
            .AppendLine($"Title: {embed.Title}")
            .AppendLine("Description:")
            .AppendLine(embed.Description)
            .AppendLine();

        foreach (var field in embed.Fields)
        {
            text.AppendLine($"[{field.Name}] {field.Value}");
        }

        return text.ToString();
    }
}
