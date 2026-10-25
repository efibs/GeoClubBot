using System.Net;
using Configuration;
using Entities;
using FluentAssertions;
using GeoClubBot.Discord.InputAdapters.Interactions.AI;
using GeoClubBot.RetrievalProbe;
using UseCases.OutputPorts.AI;
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
    public void FeedbackExport_KeepsTheBranchBeforeTheQuestion_AsTheBotReplaysIt()
    {
        // A follow-up is searched with its branch, so the replay needs the turns before it — and only
        // those: the rated answer itself was not there when the question was searched.
        var record = Record(
            ("user", "where can I find sugar cane in the Philippines?", false),
            ("assistant", "Negros [1].", false),
            ("user", "What about corn?", false),
            ("assistant", "Mindanao [2].", false));

        var question = FeedbackExport.Parse(AiFeedbackFormatter.RenderJsonLines([record]).Split('\n')).Single();

        question.IsFollowUp.Should().BeTrue();
        question.Context.Select(turn => (turn.Role, turn.Content)).Should().Equal(
            (AiTurnRole.User, "where can I find sugar cane in the Philippines?"),
            (AiTurnRole.Assistant, "Negros [1]."));
    }

    [Fact]
    public void FeedbackExport_ReadsTheExcerptsAndToolCallsOfEachAnswer()
    {
        var (offered, cited) = (Guid.NewGuid(), Guid.NewGuid());
        var calls = new[]
        {
            new UseCases.UseCases.AI.Tools.AiToolCallSummary(
                1, "search_guides", """{"query":"snow poles"}""", UseCases.UseCases.AI.Tools.AiToolCallOutcome.Answered, 2, 900, 300)
        };
        var record = Record(("user", "snow bollards?", false), ("assistant", "Snow poles [1].", false));
        record = record with
        {
            Transcript = [record.Transcript[0], record.Transcript[1] with { OfferedChunkIds = [offered, cited], CitedChunkIds = [cited], ToolCalls = calls }]
        };

        var answer = FeedbackExport.Parse(AiFeedbackFormatter.RenderJsonLines([record]).Split('\n')).Single().Record.Transcript[1];

        answer.OfferedChunkIds.Should().Equal(offered, cited);
        answer.CitedChunkIds.Should().Equal(cited);
        answer.ToolCalls.Should().Equal(calls);
    }

    [Fact]
    public void FeedbackExport_ReadsAnExportWrittenBeforeAnswersNamedTheirExcerpts()
    {
        const string line = """
            {"feedbackId":"0b7e0f8e-6c1a-4d4e-9d7a-2b1f4f1b2c3d","rating":"negative","reviewerDiscordUserId":1,"ratedDiscordMessageId":2,"conversationId":3,"channelId":4,"modelId":"m","answerDepth":1,"createdAtUtc":"2026-10-23T19:36:00+00:00","updatedAtUtc":"2026-10-23T19:36:00+00:00","transcript":[{"ordinal":0,"role":"user","authorDiscordUserId":42,"content":"round roof tiles in India?","imageUrls":[],"createdAtUtc":"2026-10-23T19:36:00+00:00"},{"ordinal":1,"role":"assistant","authorDiscordUserId":1,"content":"Central India [1].","imageUrls":[],"modelId":"m","createdAtUtc":"2026-10-23T19:36:00+00:00"}],"retrievedSourceUrls":["https://www.plonkit.net/india#cIYN"],"citedSourceUrls":["https://www.plonkit.net/india#cIYN"]}
            """;

        var answer = FeedbackExport.Parse([line]).Single().Record.Transcript[1];

        answer.OfferedChunkIds.Should().BeNull();
        answer.ToolCalls.Should().BeNull();
    }

    [Fact]
    public void FeedbackExport_GivesAFreshQuestionNoBranch() =>
        FeedbackExport.Parse(AiFeedbackFormatter.RenderJsonLines([Record(("user", "q", false), ("assistant", "a", false))]).Split('\n'))
            .Single().IsFollowUp.Should().BeFalse();

    [Fact]
    public void Arguments_LeaveTheExcerptCountToTheBot_AndReadThePlannerAndVariantOptions()
    {
        var arguments = ProbeArguments.Parse(["compare", "export.jsonl", "--allow-chat", "--variants", "grid.json"])!;

        arguments.Limit.Should().BeNull("the bot's own AI:Retrieval:ExcerptLimit applies");
        arguments.AllowChat.Should().BeTrue();
        arguments.VariantsPath.Should().Be("grid.json");
        ProbeArguments.Parse(["compare", "export.jsonl"])!.AllowChat.Should().BeFalse("chat is the bot's real cost, and asked for by name");
    }

    [Fact]
    public void Variants_ApplySettingsAsTheBotBindsThem_OnTheShippedOrThePreviousDefaults()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """
                {
                  // Comments and trailing commas, as in the bot's own settings.
                  "weights": { "CountryBoost": { "Weight": 3, "MaxCountries": 4 }, "ExcerptLimit": 12, },
                  "old": { "base": "before" },
                  // The probe's own keys, for what the bot fixes in code rather than configuration.
                  "pixels": { "pixels": 0.5, "approximate": true },
                }
                """);

            var variants = ProbeRetrieval.ReadVariants(path);

            variants.Select(variant => variant.Name).Should().Equal("weights", "old", "pixels");
            variants[0].PixelWeight.Should().BeNull();
            variants[0].Approximate.Should().BeFalse();
            variants[2].PixelWeight.Should().Be(0.5);
            variants[2].Approximate.Should().BeTrue();
            variants[2].Settings.Should().BeEquivalentTo(ProbeRetrieval.Shipped(), "the probe's keys are no settings of the bot");
            variants[0].Settings.CountryBoost.Weight.Should().Be(3);
            variants[0].Settings.CountryBoost.MaxCountries.Should().Be(4);
            variants[0].Settings.ExcerptLimit.Should().Be(12);
            variants[0].Settings.ContextualQuery.Should().Be(new AiRetrievalConfiguration().ContextualQuery);

            variants[1].Settings.ContextualQuery.Should().BeFalse("the bot searched by the question's own words before");
            variants[1].Settings.QueryPlanner.Enabled.Should().BeFalse();
            variants[1].Settings.CountryBoost.Enabled.Should().BeFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BuiltInVariants_PlanWhereTheirNamesSay_AndShipWithout()
    {
        // The planner ships off, so a column that measures it has to switch it on itself.
        var variants = CompareCommand.BuiltInVariants().ToDictionary(variant => variant.Name, variant => variant.Settings);

        variants.Where(pair => pair.Key.StartsWith("+planner", StringComparison.Ordinal))
            .Should().HaveCount(3).And.OnlyContain(pair => pair.Value.QueryPlanner.Enabled);
        variants["+planner"].ExcerptLimit.Should().Be(ProbeRetrieval.Before().ExcerptLimit);
        variants["+planner, 12"].ExcerptLimit.Should().Be(12);
        variants["+headings"].QueryPlanner.Enabled.Should().BeFalse();
        variants["shipped"].Should().BeEquivalentTo(ProbeRetrieval.Shipped());
        variants["shipped"].QueryPlanner.Enabled.Should().BeFalse();
    }

    [Fact]
    public void Override_LeavesTheSettingsItCopied_AsTheyWere()
    {
        var shipped = ProbeRetrieval.Shipped();

        var changed = ProbeRetrieval.Override(shipped, new Dictionary<string, string?> { ["CountryBoost:Weight"] = "3" });

        changed.CountryBoost.Weight.Should().Be(3);
        shipped.CountryBoost.Weight.Should().Be(new AiRetrievalConfiguration().CountryBoost.Weight);
    }

    [Fact]
    public void Targets_ReadPatternsCountriesAndChunks_AndPickTheFirstThatFits()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """
                {
                  "khasi": "khasi|kesiya",
                  "what about corn": { "match": "corn|maize", "countries": ["Philippines"], "chunks": ["philippines#9hUv"] }
                }
                """);

            var targets = ProbeTargets.Read(path);
            var corn = ProbeTargets.For(targets, "What about CORN?");

            corn!.OnTarget.IsMatch("Maize is grown on Mindanao.").Should().BeTrue();
            corn.Countries.Should().Equal("philippines");
            corn.IsChunk(Hit("https://www.plonkit.net/philippines#9hUv", "Corn."), corn.Chunks[0].Pattern).Should().BeTrue();
            ProbeTargets.For(targets, "where can I find Khasi pines?")!.Countries.Should().BeEmpty();
            ProbeTargets.For(targets, "something else").Should().BeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Targets_NameTheProblem_WhenAPatternIsBroken()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """{ "corn": { "match": "corn(" } }""");

            FluentActions.Invoking(() => ProbeTargets.Read(path)).Should().Throw<ProbeException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Targets_OfTheLatestExport_AllParse() =>
        // The committed file is what the measurements in AiGuide.md were counted with.
        ProbeTargets.Read(Path.Combine(RepositoryRoot(), "Tools", "GeoClubBot.RetrievalProbe", "Targets", "feedback-20261023.json"))
            .Should().NotBeEmpty();

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

    [Fact]
    public async Task EmbeddingsOnlyHandler_LetsThePlannerThrough_OnlyWhenAskedTo()
    {
        var guard = new EmbeddingsOnlyHandler(new StubHandler(), allowChat: true);
        using var client = new HttpClient(guard) { BaseAddress = new Uri("https://openrouter.ai") };

        (await client.PostAsync(EmbeddingsOnlyHandler.ChatCompletionsPath, new StringContent("{}"))).StatusCode
            .Should().Be(HttpStatusCode.OK);
        (await client.GetAsync(EmbeddingsOnlyHandler.ModelsPath)).StatusCode.Should().Be(HttpStatusCode.OK,
            "the planner picks its model from the roster, as the bot does");

        // Still nothing else: no other endpoint, and no other method on the allowed ones.
        await FluentActions.Awaiting(() => client.GetAsync(EmbeddingsOnlyHandler.ChatCompletionsPath))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => client.PostAsync("/api/v1/credits", new StringContent("{}")))
            .Should().ThrowAsync<InvalidOperationException>();

        guard.RequestsSent.Should().Be(2);
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

    private static KnowledgeHit Hit(string sourceUrl, string text) =>
        new(Guid.NewGuid(), 0.5f, KnowledgeChunkKind.Text, text, sourceUrl, ImageUrl: null,
            "Plonk It", "philippines", "Philippines > Agriculture", "Plonk It team", 0);

    /// <summary>The repository root, found from the test assembly's directory upwards.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoClubBot.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("GeoClubBot.sln not found above the test assembly.");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
