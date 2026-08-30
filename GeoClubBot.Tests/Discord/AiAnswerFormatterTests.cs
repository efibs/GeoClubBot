using System.Text;
using Discord;
using FluentAssertions;
using GeoClubBot.Discord.InputAdapters.Interactions.AI;
using UseCases.UseCases.AI.Conversations;
using Xunit;
using static VerifyXunit.Verifier;

namespace GeoClubBot.Tests.Discord;

public sealed class AiAnswerFormatterTests
{
    [Fact]
    public Task Render_ShowsTheAnswerWithItsModelAttribution()
    {
        var answer = new AiAnswer(
            "Ghanaian bollards are short, white, and squared off at the top.",
            [],
            [],
            "google/gemma-4-31b-it:free",
            ConversationId: 100,
            Depth: 1,
            IsLongThread: false);

        return Verify(Render(AiAnswerFormatter.Render(answer)));
    }

    [Fact]
    public Task Render_AttachesCitedGuideImagesAsLinkedEmbeds()
    {
        var answer = new AiAnswer(
            "The area codes are laid out by governorate.",
            [
                new AiAnswerImage("https://i.imgur.com/map.png", "https://docs.google.com/document/d/abc", "Tunisia area codes"),
                new AiAnswerImage("https://i.imgur.com/signs.png", "https://www.plonkit.net/tunisia", null)
            ],
            [],
            "nvidia/nemotron-3.5-content-safety:free",
            ConversationId: 100,
            Depth: 3,
            IsLongThread: false);

        return Verify(Render(AiAnswerFormatter.Render(answer)));
    }

    [Fact]
    public Task Render_ResolvesCitationMarkersIntoNamedClickableSources()
    {
        // A bare "[1]" tells the reader nothing and leads nowhere. Each marker the answer uses is
        // listed below it with the heading the model saw and a link to the guide it came from.
        var answer = new AiAnswer(
            "Roads starting with MR are exclusive to Eswatini [1], and the MR9 runs through dark, "
            + "wooded highlands [3].",
            [],
            [
                new AiAnswerSource(1, "Eswatini > Identifying", "https://www.plonkit.net/eswatini#m1jr"),
                new AiAnswerSource(3, "Eswatini > Regional clues", "https://www.plonkit.net/eswatini#1chu")
            ],
            "minimax/minimax-m3:free",
            ConversationId: 100,
            Depth: 1,
            IsLongThread: false);

        return Verify(Render(AiAnswerFormatter.Render(answer)));
    }

    [Fact]
    public Task Render_ListsRelatedGuides_WhenTheModelCitedNothing()
    {
        // Unnumbered, because there is no marker in the prose for a number to point at, and headed so
        // the list does not read as citations that lost their numbers.
        var answer = new AiAnswer(
            "Roads starting with MR are exclusive to Eswatini.",
            [],
            [
                new AiAnswerSource(null, "Eswatini > Identifying", "https://www.plonkit.net/eswatini#m1jr"),
                new AiAnswerSource(null, "Eswatini > Regional clues", "https://www.plonkit.net/eswatini#1chu")
            ],
            "minimax/minimax-m3:free",
            ConversationId: 100,
            Depth: 1,
            IsLongThread: false);

        return Verify(Render(AiAnswerFormatter.Render(answer)));
    }

    [Fact]
    public Task Render_SuggestsAFreshThread_WhenTheBranchIsLong()
    {
        var answer = new AiAnswer("Still here.", [], [], "test/model", ConversationId: 100, Depth: 21, IsLongThread: true);

        return Verify(Render(AiAnswerFormatter.Render(answer)));
    }

    [Fact]
    public void Render_SplitsALongAnswerAcrossMessages()
    {
        // Discord rejects anything over 2000 characters, so an answer that exceeds it must arrive as
        // several messages rather than being truncated or dropped.
        var answer = new AiAnswer(
            string.Join("\n", Enumerable.Repeat("A fairly long line about bollards.", 120)),
            [], [], "test/model", ConversationId: 100, Depth: 1, IsLongThread: false);

        var rendering = AiAnswerFormatter.Render(answer);

        rendering.MessageChunks.Should().HaveCountGreaterThan(1);
        rendering.MessageChunks.Should().OnlyContain(chunk => chunk.Length <= 2000);
        rendering.MessageChunks[^1].Should().Contain("via test/model", "attribution belongs on the final message");
    }

    [Fact]
    public void Render_SubstitutesPlaceholderText_WhenTheModelReturnsNothing()
    {
        // Discord rejects an empty message outright, which would surface as an exception rather than
        // a visible failure.
        var answer = new AiAnswer("   ", [], [], "test/model", ConversationId: 100, Depth: 1, IsLongThread: false);

        var rendering = AiAnswerFormatter.Render(answer);

        rendering.MessageChunks[0].Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>Flattens the rendering into plain text so one snapshot captures the whole layout.</summary>
    private static string Render(AiAnswerRendering rendering)
    {
        var builder = new StringBuilder();

        for (var index = 0; index < rendering.MessageChunks.Count; index++)
        {
            builder.AppendLine($"--- message {index + 1} ---");
            builder.AppendLine(rendering.MessageChunks[index]);
        }

        foreach (var embed in rendering.Embeds)
        {
            builder.AppendLine("--- embed ---");
            builder.AppendLine($"Title: {embed.Title}");
            builder.AppendLine($"Image: {embed.Image?.Url}");
            builder.AppendLine($"Url:   {embed.Url}");
        }

        return builder.ToString();
    }
}
