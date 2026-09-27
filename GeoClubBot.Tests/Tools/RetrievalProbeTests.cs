using System.Net;
using Configuration;
using FluentAssertions;
using GeoClubBot.Discord.InputAdapters.Interactions.AI;
using GeoClubBot.RetrievalProbe;
using UseCases.UseCases.AI.Feedback;
using Xunit;

namespace GeoClubBot.Tests.Tools;

/// <summary>
/// The parts of the retrieval probe that need no index: reading the bot's exports, its command line,
/// and the guard that keeps everything but embeddings from leaving for OpenRouter.
/// </summary>
public sealed class RetrievalProbeTests
{
    [Fact]
    public void FeedbackExport_ReadsWhatTheBotExports_AndTakesTheQuestionTheRatedAnswerAnswered()
    {
        // A follow-up is retrieved for by its own words, so replaying the conversation's opening
        // question would measure the wrong search. The export is written by the bot's own formatter,
        // so the reader cannot drift from the writer unnoticed.
        var record = Record(
            ("user", "what writing system does Tripura use?", false),
            ("assistant", "Bengali-Assamese [1].", false),
            ("user", "Is this Bengali?", true),
            ("assistant", "Yes [1].", false),
            ("user", "how can I tell?", false),
            ("assistant", "The line over the letters [2].", false));

        var questions = FeedbackExport.Parse(AiFeedbackFormatter.RenderJsonLines([record]).Split('\n'));

        questions.Should().ContainSingle();
        questions[0].Question.Should().Be("how can I tell?");
        questions[0].QuestionHadScreenshot.Should().BeFalse();
        questions[0].IsNegative.Should().BeTrue();
        questions[0].Record.Comment.Should().Be("Hindi has the line too.");
        questions[0].Record.RetrievedSourceUrls.Should().Equal("https://www.plonkit.net/india#F2x8", "https://docs/b");
    }

    [Fact]
    public void FeedbackExport_NotesAQuestionThatCarriedAScreenshot()
    {
        var record = Record(("user", "where is this?", true), ("assistant", "Sulawesi.", false));

        FeedbackExport.Parse(AiFeedbackFormatter.RenderJsonLines([record]).Split('\n'))
            .Single().QuestionHadScreenshot.Should().BeTrue("its pixels were searched too, and can no longer be");
    }

    [Fact]
    public void FeedbackExport_NamesTheLineThatIsNotARecord()
    {
        var valid = AiFeedbackFormatter.RenderJsonLines([Record(("user", "q", false), ("assistant", "a", false))]).Trim();

        var act = () => FeedbackExport.Parse([valid, "", "not json"]);

        act.Should().Throw<ProbeException>().WithMessage("*Line 3*");
    }

    [Fact]
    public void Arguments_ReadCommandTargetAndOptions()
    {
        var arguments = ProbeArguments.Parse(["replay", "ai_feedback/export.jsonl", "--rating", "bad", "--limit", "5"]);

        arguments.Should().NotBeNull();
        arguments!.Command.Should().Be("replay");
        arguments.Target.Should().Be("ai_feedback/export.jsonl");
        arguments.Rating.Should().Be("negative", "the export spells ratings positive and negative");
        arguments.Limit.Should().Be(5);
    }

    [Fact]
    public void Arguments_CollectEveryVariant() =>
        ProbeArguments.Parse(["similarity", "--variant", "one wording", "--variant", "another"])!
            .Variants.Should().Equal("one wording", "another");

    [Theory]
    [InlineData("replay --limit abc")]
    [InlineData("replay --limit")]
    [InlineData("replay --rating meh")]
    [InlineData("replay --bogus")]
    public void Arguments_RefuseWhatTheyCannotRead(string commandLine) =>
        // A value that fails to parse must not quietly become the command's target instead.
        ProbeArguments.Parse(commandLine.Split(' ')).Should().BeNull();

    [Fact]
    public void Arguments_NeedACommand() =>
        ProbeArguments.Parse([]).Should().BeNull();

    [Fact]
    public void Settings_NameTheCollectionTheBotWritesTo() =>
        // The name the production index has, measured on 2026-09-27: the probe reads the bot's own
        // collection without being told which one it is.
        ProbeSettings.BotCollectionName(new AiConfiguration()).Should().Be("geo-knowledge-852af1c5-2048");

    [Fact]
    public async Task EmbeddingsOnlyHandler_LetsOnlyEmbeddingsLeave_AndCountsThem()
    {
        // A chat completion is the bot's real cost; the probe must not be able to send one by mistake.
        var guard = new EmbeddingsOnlyHandler(new StubHandler());
        using var client = new HttpClient(guard) { BaseAddress = new Uri("https://openrouter.ai") };

        (await client.PostAsync(EmbeddingsOnlyHandler.EmbeddingsPath, new StringContent("{}"))).StatusCode
            .Should().Be(HttpStatusCode.OK);

        await FluentActions.Awaiting(() => client.PostAsync("/api/v1/chat/completions", new StringContent("{}")))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => client.GetAsync(EmbeddingsOnlyHandler.EmbeddingsPath))
            .Should().ThrowAsync<InvalidOperationException>();

        guard.RequestsSent.Should().Be(1, "only what left the process is counted");
    }

    private static AiFeedbackRecord Record(params (string Role, string Content, bool WithImage)[] turns) =>
        new(Guid.NewGuid(), "negative", "Hindi has the line too.", 1, 2, 3, 4, 5, "test/model", turns.Length - 1,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            [.. turns.Select((turn, ordinal) => new AiFeedbackTurnRecord(
                ordinal, turn.Role, 42, turn.Content,
                turn.WithImage ? ["https://cdn.discordapp.com/attachments/1/2/image.png"] : [],
                turn.Role == "assistant" ? "test/model" : null, DateTimeOffset.UtcNow))],
            ["https://www.plonkit.net/india#F2x8", "https://docs/b"],
            ["https://www.plonkit.net/india#F2x8"]);

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
