using System.Net;
using Configuration;
using Entities;
using FluentAssertions;
using GeoClubBot.Discord.InputAdapters.Interactions.AI;
using GeoClubBot.RetrievalProbe;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Conversations;
using UseCases.UseCases.AI.Feedback;
using UseCases.UseCases.AI.Tools;
using Xunit;

namespace GeoClubBot.Tests.Tools;

/// <summary>
/// The parts of the probe's <c>answer</c> command that need neither a model nor an index: its command
/// line, the guard it widens, the branch it rebuilds for the bot, the settings it reads, the judge's
/// prompt and reply, and the report.
/// </summary>
public sealed class RetrievalProbeAnswerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Arguments_ReadTheAnswerOptions()
    {
        var arguments = ProbeArguments.Parse(
        [
            "answer", "export.jsonl", "--allow-chat", "--set", "AI:Tools:Enabled=false", "--set", "AI:Retrieval:ExcerptLimit=8",
            "--only", "1-25,31", "--parallel", "3", "--judge-model", "a/one, b/two", "--limit", "5"
        ])!;

        arguments.Command.Should().Be("answer");
        arguments.Target.Should().Be("export.jsonl");
        arguments.AllowChat.Should().BeTrue();
        arguments.Overrides.Should().Equal("AI:Tools:Enabled=false", "AI:Retrieval:ExcerptLimit=8");
        arguments.Only!.Numbers.Should().Equal((1, 25), (31, 31));
        arguments.Parallelism.Should().Be(3);
        arguments.JudgeModels.Should().Equal("a/one", "b/two");
        arguments.Judge.Should().BeTrue("naming the judge's models asks for the judge");
        arguments.Limit.Should().Be(5);
    }

    [Fact]
    public void Arguments_DefaultToTwoAtATime_AndNoJudge()
    {
        var arguments = ProbeArguments.Parse(["answer", "export.jsonl", "--allow-chat"])!;

        arguments.Parallelism.Should().Be(2);
        arguments.Judge.Should().BeFalse("the judge costs a request per answer, and is asked for by name");
        arguments.Overrides.Should().BeEmpty();
        arguments.Only.Should().BeNull();
    }

    [Fact]
    public void Arguments_TakeASettingWithAnEmptyValue() =>
        // Clearing a list entry is how the bot's settings are overridden one index at a time.
        ProbeArguments.Parse(["answer", "x", "--set", "AI:OpenRouter:PreferredModelPrefixes:0="])!
            .Overrides.Should().Equal("AI:OpenRouter:PreferredModelPrefixes:0=");

    [Theory]
    [InlineData("answer x --set AI:Tools:Enabled")]
    [InlineData("answer x --set =false")]
    [InlineData("answer x --set")]
    [InlineData("answer x --parallel 0")]
    [InlineData("answer x --parallel 9")]
    [InlineData("answer x --parallel two")]
    [InlineData("answer x --only")]
    [InlineData("answer x --only yesterday")]
    [InlineData("answer x --only 25-1")]
    [InlineData("answer x --only after:27.09.2026")]
    public void Arguments_RefuseAnswerOptionsTheyCannotRead(string commandLine) =>
        ProbeArguments.Parse(commandLine.Split(' ')).Should().BeNull();

    [Fact]
    public void RecordFilter_PicksByNumberRangeAndId_AndNarrowsByDate()
    {
        var early = Rated(number: 3, created: new DateTimeOffset(2026, 9, 27, 18, 0, 0, TimeSpan.Zero));
        var late = Rated(number: 12, created: new DateTimeOffset(2026, 10, 6, 9, 0, 0, TimeSpan.Zero));

        RecordFilter.Parse("1-5")!.Matches(early).Should().BeTrue();
        RecordFilter.Parse("1-5")!.Matches(late).Should().BeFalse();
        RecordFilter.Parse("12")!.Matches(late).Should().BeTrue();
        RecordFilter.Parse(late.Record.FeedbackId.ToString("N")[..8])!.Matches(late).Should().BeTrue();
        RecordFilter.Parse(late.Record.FeedbackId.ToString("D")[..13])!.Matches(late).Should().BeTrue("an id is matched however it is spelled");

        // Dates narrow: "after" a day is from the next one, so the first export's day is left out.
        RecordFilter.Parse("after:2026-09-27")!.Matches(early).Should().BeFalse();
        RecordFilter.Parse("after:2026-09-27")!.Matches(late).Should().BeTrue();
        RecordFilter.Parse("1-20,before:2026-10-01")!.Matches(late).Should().BeFalse();
        RecordFilter.Parse("1-20,before:2026-10-01")!.Matches(early).Should().BeTrue();
    }

    [Theory]
    [InlineData("/api/v1/models/nvidia/nemotron-3-ultra-550b-a55b:free/endpoints", true)]
    [InlineData("/api/v1/models/nvidia/nemotron-3-ultra-550b-a55b%3Afree/endpoints", true)]
    [InlineData("/api/v1/models/nvidia/endpoints", false)]
    [InlineData("/api/v1/models/a/b/c/endpoints", false)]
    [InlineData("/api/v1/models/a/../endpoints", false)]
    [InlineData("/api/v1/models/a/b%2Fc/endpoints", false)]
    [InlineData("/api/v1/models//b/endpoints", false)]
    [InlineData("/api/v1/models/a/b/endpoints/x", false)]
    [InlineData("/api/v1/keys/a/b/endpoints", false)]
    public void Guard_KnowsAModelsHealthRead(string path, bool isHealthRead) =>
        EmbeddingsOnlyHandler.IsModelEndpointsPath(path).Should().Be(isHealthRead);

    [Fact]
    public async Task Guard_LetsTheBotsAnsweringReadsThrough_OnlyWhenAskedTo_AndCountsThemForTheAnswer()
    {
        var guard = new EmbeddingsOnlyHandler(new StubHandler(), allowChat: true);
        using var client = new HttpClient(guard) { BaseAddress = new Uri("https://openrouter.ai") };
        var attribution = new RequestAttribution();
        RequestAttribution.Current = attribution;

        (await client.PostAsync(EmbeddingsOnlyHandler.EmbeddingsPath, new StringContent("{}"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync(EmbeddingsOnlyHandler.ChatCompletionsPath, new StringContent("{}"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/v1/models/nvidia/nemotron-3-ultra:free/endpoints")).StatusCode.Should().Be(HttpStatusCode.OK,
            "the model catalog reads each model's provider health, as the bot does");

        // Nothing that changes the account, and no other method on the allowed routes.
        await FluentActions.Awaiting(() => client.PostAsync("/api/v1/models/nvidia/x/endpoints", new StringContent("{}")))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => client.GetAsync("/api/v1/keys"))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => client.DeleteAsync("/api/v1/models"))
            .Should().ThrowAsync<InvalidOperationException>();

        guard.RequestsSent.Should().Be(3);
        (attribution.Embeddings, attribution.Chats, attribution.Roster).Should().Be((1, 1, 1));
        RequestAttribution.Current = null;
    }

    [Fact]
    public async Task Guard_StillRefusesAHealthRead_WithoutAllowChat()
    {
        var guard = new EmbeddingsOnlyHandler(new StubHandler());
        using var client = new HttpClient(guard) { BaseAddress = new Uri("https://openrouter.ai") };

        await FluentActions.Awaiting(() => client.GetAsync("/api/v1/models/nvidia/x/endpoints"))
            .Should().ThrowAsync<InvalidOperationException>();
        guard.RequestsSent.Should().Be(0);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, 0, false, 2)]
    [InlineData(HttpStatusCode.GatewayTimeout, 1, false, 4)]
    [InlineData(HttpStatusCode.GatewayTimeout, 2, false, null)]
    [InlineData(HttpStatusCode.TooManyRequests, 0, false, 41)]
    [InlineData(HttpStatusCode.TooManyRequests, 0, true, null)]
    [InlineData(HttpStatusCode.TooManyRequests, 1, false, null)]
    [InlineData(HttpStatusCode.BadRequest, 0, false, null)]
    [InlineData(HttpStatusCode.OK, 0, false, null)]
    public void Pacing_RetriesWhatTheBotsPipelineRetries(HttpStatusCode status, int attempt, bool upstreamLimit, int? seconds)
    {
        // 20 s past the minute: a 429 waits for the next window, a second into it.
        var now = new DateTimeOffset(2026, 10, 29, 12, 0, 20, TimeSpan.Zero);

        PacedRetryHandler.RetryDelay(status, attempt, upstreamLimit, retryAfter: null, now)
            .Should().Be(seconds is { } wait ? TimeSpan.FromSeconds(wait) : null);
    }

    [Fact]
    public void Pacing_WaitsAsLongAsTheProviderSays_WithinAMinute()
    {
        PacedRetryHandler.RetryDelay(HttpStatusCode.TooManyRequests, 0, false, TimeSpan.FromSeconds(12), Now)
            .Should().Be(TimeSpan.FromSeconds(12));
        PacedRetryHandler.RetryDelay(HttpStatusCode.TooManyRequests, 0, false, TimeSpan.FromMinutes(10), Now)
            .Should().BeNull("an hour's limit is not waited out in a replay");
    }

    [Fact]
    public async Task Pacing_HandsOnWhatItDoesNotRetry_AndNeverRetriesARefusal()
    {
        var stub = new StubHandler(HttpStatusCode.BadRequest);
        var guard = new EmbeddingsOnlyHandler(stub, allowChat: true);
        using var client = new HttpClient(new PacedRetryHandler(guard, 18, TimeSpan.FromSeconds(5))) { BaseAddress = new Uri("https://openrouter.ai") };

        (await client.PostAsync(EmbeddingsOnlyHandler.ChatCompletionsPath, new StringContent("{}"))).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
        await FluentActions.Awaiting(() => client.PostAsync("/api/v1/credits", new StringContent("{}")))
            .Should().ThrowAsync<InvalidOperationException>();

        stub.Sent.Should().Be(1, "a 400 is the request's fault and is not asked again, and a refused request never leaves");
    }

    [Fact]
    public async Task Branch_IsWhatTheBotReadsAFollowUpsBranchFrom()
    {
        // The bot walks the stored tree from the message replied to, so the seeded turns have to form
        // that path: the follow-up then gets its branch, and the excerpts the answer before cited.
        var cited = Guid.NewGuid();
        var record = Record(
            ("user", "where can I find sugar cane in the Philippines?", null),
            ("assistant", "Negros [1].", null),
            ("user", "What about corn?", null),
            ("assistant", "Mindanao [1].", null));
        record = record with
        {
            Transcript = [record.Transcript[0], record.Transcript[1] with { CitedChunkIds = [cited] }, record.Transcript[2], record.Transcript[3]]
        };
        var rated = Parse(record);

        var branch = AnswerBranch.Create(rated, Now);

        branch.Turns.Should().HaveCount(2);
        branch.Command.Content.Should().Be("What about corn?");
        branch.Command.ParentDiscordMessageId.Should().Be(branch.Turns[^1].DiscordMessageId);
        branch.Notes.Should().BeEmpty();

        var repository = new InMemoryConversationRepository(branch.Turns);
        var parent = await repository.ReadByMessageIdAsync(branch.Command.ParentDiscordMessageId!.Value);
        var context = ConversationContextBuilder.Build(
            await repository.ReadConversationAsync(parent!.ConversationId), parent.DiscordMessageId, new AiConversationConfiguration(), Now);

        context.Turns.Select(turn => (turn.Role, turn.Content)).Should().Equal(
            (AiTurnRole.User, "where can I find sugar cane in the Philippines?"),
            (AiTurnRole.Assistant, "Negros [1]."));
        context.PreviousCitedChunkIds.Should().Equal([cited], "a follow-up is offered again what the answer before it cited");
    }

    [Fact]
    public void Branch_OfAFreshQuestion_RepliesToNothing()
    {
        var branch = AnswerBranch.Create(Parse(Record(("user", "what is a Kyushu insulator?", null), ("assistant", "A pancake [1].", null))), Now);

        branch.Turns.Should().BeEmpty();
        branch.Command.ParentDiscordMessageId.Should().BeNull();
        branch.Command.Content.Should().Be("what is a Kyushu insulator?");
    }

    [Fact]
    public void Branch_LeavesOutScreenshotsWhoseLinksHaveExpired_AndSaysSo()
    {
        const string expired = "https://cdn.discordapp.com/attachments/1/2/image.png?ex=68d2f6a1&is=1&hm=2";
        const string live = "https://cdn.discordapp.com/attachments/1/2/image.png?ex=6c000000&is=1&hm=2";
        var record = Record(
            ("user", "Is this Bengali?", expired),
            ("assistant", "Yes [1].", null),
            ("user", "and this one?", live));

        var branch = AnswerBranch.Create(Parse(record), Now);

        branch.Turns[0].ImageUrls.Should().BeEmpty("a model asked to read a dead link fails the whole request");
        branch.Command.AttachmentImageUrls.Should().Equal(live);
        branch.Notes.Should().ContainSingle().Which.Should().Contain("earlier in the branch");
    }

    [Theory]
    [InlineData("https://cdn.discordapp.com/attachments/1/2/a.png?ex=68d2f6a1&is=1", true)]
    [InlineData("https://media.discordapp.net/attachments/1/2/a.png?ex=6c000000", false)]
    [InlineData("https://cdn.discordapp.com/attachments/1/2/a.png", true)]
    [InlineData("https://i.imgur.com/abc.png", false)]
    public void Branch_ReadsADiscordLinksExpiry(string url, bool expired) =>
        AnswerBranch.IsExpiredDiscordAttachment(url, Now).Should().Be(expired);

    [Fact]
    public void Settings_AreTheBotsOwn_WithTheOverridesOnTop()
    {
        var path = WriteSettings("""
            {
              "AI": {
                /* comments, as the bot's file has */
                "MaxImagesInReply": 6,
                "Tools": { "Enabled": true, "MaxToolRounds": 3 },
                "Retrieval": { "ExcerptLimit": 10 },
                "OpenRouter": { "ApiKey": "", "DailyRequestBudget": 45 }
              }
            }
            """);

        var shipped = BotSettings.Load(path, [], "sk-secret");
        var overridden = BotSettings.Load(path, ["AI:Tools:Enabled=false", "AI__Retrieval__ExcerptLimit=8"], "sk-secret");

        shipped.Tools.Enabled.Should().BeTrue();
        shipped.Retrieval.ExcerptLimit.Should().Be(10);
        shipped.Ai.OpenRouter.ApiKey.Should().Be("sk-secret");
        overridden.Tools.Enabled.Should().BeFalse();
        overridden.Retrieval.ExcerptLimit.Should().Be(8, "the environment-variable spelling is read too");
        overridden.Fingerprint.Should().NotBe(shipped.Fingerprint, "an answer is cached per settings");
        BotSettings.Load(path, [], "another-key").Fingerprint.Should().Be(shipped.Fingerprint, "the key is not a setting of the answer");
    }

    [Theory]
    [InlineData("Discord:Token=x")]
    [InlineData("AI:Tools:MaxToolRounds=9")]
    public void Settings_RefuseWhatTheBotWouldNotStartWith(string setting)
    {
        var path = WriteSettings("""{ "AI": { "Tools": { "Enabled": true } } }""");

        FluentActions.Invoking(() => BotSettings.Load(path, [setting], null)).Should().Throw<ProbeException>();
    }

    [Fact]
    public void Excerpts_AreThePromptsAndEveryToolResult()
    {
        IReadOnlyList<AiChatMessage> messages =
        [
            AiChatMessage.System("You are a GeoGuessr assistant."),
            AiChatMessage.User("Guide excerpts:\n[1] Philippines > Agriculture\nCorn in Mindanao.\n(source: https://www.plonkit.net/philippines#9hUv)\n\nQuestion: What about corn?"),
            AiChatMessage.AssistantToolCalls(null, [new AiToolCall("c1", "search_guides", "{}")]),
            AiChatMessage.ToolResult("c1", "[2] Philippines > Corn\nNorthern Luzon.")
        ];

        var excerpts = AnswerRun.DescribeExcerpts(messages);

        excerpts.Should().StartWith("Guide excerpts:").And.Contain("Corn in Mindanao.").And.Contain("Tool result:\n[2] Philippines > Corn");
        excerpts.Should().NotContain("What about corn?", "the judge is given the question separately");
    }

    [Fact]
    public void Excerpts_AreTheQuestionsPrompt_NotTheAnswerNowAfterTheLastRound()
    {
        // The call that has to answer after the tool rounds ends with a user message of its own.
        IReadOnlyList<AiChatMessage> messages =
        [
            AiChatMessage.System("You are a GeoGuessr assistant."),
            AiChatMessage.User("Guide excerpts:\n[1] Philippines > Agriculture\nCorn in Mindanao.\n(source: https://www.plonkit.net/philippines#9hUv)\n\nQuestion: What about corn?"),
            AiChatMessage.AssistantToolCalls(null, [new AiToolCall("c1", "search_guides", "{}")]),
            AiChatMessage.ToolResult("c1", "[2] Philippines > Corn\nNorthern Luzon."),
            AiChatMessage.User(AnswerTools.AnswerNow)
        ];

        var excerpts = AnswerRun.DescribeExcerpts(messages);

        excerpts.Should().StartWith("Guide excerpts:").And.Contain("Corn in Mindanao.").And.Contain("Tool result:\n[2] Philippines > Corn")
            .And.NotContain(AnswerTools.AnswerNow);
    }

    [Fact]
    public void Judge_IsShownTheQuestionTheCommentAndBothAnswers()
    {
        var rated = Parse(Record(("user", "where can I find Khasi pines?", null), ("assistant", "Meghalaya [1].", null)));
        var run = Run(rated, Answer("Meghalaya and Vietnam [1].")) with { ModelText = "Meghalaya [3] and northern Vietnam [7]." };

        var prompt = AnswerJudge.BuildMessages(rated, run, "Meghalaya and Vietnam [1].", ["[1] Khasi pine — <https://x/1.webp>"])[1].ToPlainText();

        prompt.Should().Contain("Question: where can I find Khasi pines?")
            .And.Contain("\"I would have liked an image.\"")
            .And.Contain("Meghalaya [1].")
            .And.Contain("Meghalaya [3] and northern Vietnam [7].", "the model's own numbers match the excerpts the judge is shown")
            .And.Contain("[1] Khasi pine")
            .And.Contain("Guide excerpts:");
    }

    [Theory]
    [InlineData("""{"verdict":"better","addressesComment":true,"hallucinationRisk":"low","reasons":"It names Vietnam."}""", "better", true, "low")]
    [InlineData("Thinking about it…\n```json\n{\"verdict\": \"Worse\", \"addressesComment\": null, \"hallucinationRisk\": \"Moderate\", \"reasons\": [\"Vaguer.\", \"No map.\"]}\n```", "worse", null, "medium")]
    [InlineData("""{"note": "{not this}"} then {"verdict":"equal","addresses_comment":"no","hallucination_risk":"high","reason":"Same."}""", "same", false, "high")]
    public void Judge_ReadsAVerdict_HoweverItIsWrapped(string reply, string verdict, bool? addresses, string risk)
    {
        var read = AnswerJudge.Parse(reply, "judge/model", 1.5);

        read.Verdict.Should().Be(verdict);
        read.AddressesComment.Should().Be(addresses);
        read.HallucinationRisk.Should().Be(risk);
        read.Reasons.Should().NotBeEmpty();
        read.Model.Should().Be("judge/model");
    }

    [Fact]
    public void Judge_KeepsAReplyItCannotRead()
    {
        var read = AnswerJudge.Parse("The new answer is clearly better.", "judge/model", 1);

        read.IsReadable.Should().BeFalse();
        read.Raw.Should().Be("The new answer is clearly better.");
    }

    [Fact]
    public void Report_ShowsTheSummary_AndEachAnswerAsDiscordWouldPostIt()
    {
        var rated = Parse(Record(("user", "where can I find Khasi pines?", null), ("assistant", "Meghalaya [1].", null)));
        var answer = Answer("Meghalaya [1] and northern Vietnam [2].") with
        {
            Images = [new AiAnswerImage(1, "https://bot.example/api/v1/ai/images/" + new string('a', 64) + ".webp", "https://www.plonkit.net/india#lQsr", "India")],
            Sources = [new AiAnswerSource(1, "India > Vegetation", "https://www.plonkit.net/india#lQsr", IsImage: true), new AiAnswerSource(2, "Vietnam > Trees", "https://www.plonkit.net/vietnam#Bj0v")],
            ToolCalls = [new AiToolCallSummary(1, "species_range", """{"species":"Pinus kesiya"}""", AiToolCallOutcome.Answered, 1, 640, 420)],
            RetrievedSourceUrls = ["https://www.plonkit.net/india#lQsr", "https://www.plonkit.net/vietnam#Bj0v"],
            CitedSourceUrls = ["https://www.plonkit.net/india#lQsr", "https://www.plonkit.net/vietnam#Bj0v"]
        };
        var failed = Parse(Record(("user", "What about corn?", null), ("assistant", "Mindanao.", null)), number: 2);
        var verdict = new JudgeVerdict("better", true, "low", "Adds Vietnam and a map.", "judge/model", 3);

        var report = AnswerReport.Render(Header(judged: true),
        [
            new AnswerReportRow(rated, Run(rated, answer), verdict),
            new AnswerReportRow(failed, Run(failed, null) with { ErrorCode = "ai.provider_unreachable", ErrorMessage = "Could not reach the AI provider." }, null)
        ]);

        report.Should().Contain("# Answer replay: 2 rated answer(s) of export.jsonl")
            .And.Contain("with `AI:Tools:Enabled=false`")
            .And.Contain("**Its verdicts are indicative**")
            .And.Contain("| better | 1 | 1 | 0 |")
            .And.Contain("| not judged | 1 | 1 | 0 |")
            .And.Contain("- Addresses the member's comment: 1 of 1")
            .And.Contain("species_range 1")
            .And.Contain("failed: 1 (#2 ai.provider_unreachable)");

        // The new answer as posted: the prose, the numbered sources beneath it, the model that wrote it.
        report.Should().Contain("> Meghalaya [1] and northern Vietnam [2].")
            .And.Contain("> -# [1] 🖼️ [India > Vegetation](https://www.plonkit.net/india#lQsr)")
            .And.Contain("> -# via test/new-model")
            .And.Contain("[1] India — <https://bot.example/api/v1/ai/images/")
            .And.Contain("(relayed: production uploads it from its disk)")
            .And.Contain("| 1 | species_range | `{\"species\":\"Pinus kesiya\"}` | Answered | 1 | 640 | 420 |")
            .And.Contain("**Judge** (judge/model): **better**")
            .And.Contain("Offered then, not now: <https://docs/b>")
            .And.Contain("### New answer: failed after")
            .And.Contain("`ai.provider_unreachable`: Could not reach the AI provider.");
    }

    [Fact]
    public void Report_WithoutTheJudge_HasNoVerdictColumns()
    {
        var rated = Parse(Record(("user", "q?", null), ("assistant", "a.", null)));

        var report = AnswerReport.Render(Header(judged: false), [new AnswerReportRow(rated, Run(rated, Answer("b.")), null)]);

        report.Should().NotContain("| verdict |").And.NotContain("Judge:");
        report.Should().Contain("| # | rating | question | old model | new model | tool calls | offered → cited (old) | pictures | s | requests |");
    }

    private static AnswerReportHeader Header(bool judged)
    {
        var settings = BotSettings.Load(WriteSettings("""{ "AI": { "Tools": { "Enabled": false } } }"""), ["AI:Tools:Enabled=false"], null);
        return new AnswerReportHeader(
            "export.jsonl", "geo-knowledge-x", Now, settings, ["a/model"], [], judged ? ["judge/model"] : null, 11, judged ? 1 : 0);
    }

    private static AiAnswer Answer(string text) =>
        new(text, [], [new AiAnswerSource(1, "Guide", "https://www.plonkit.net/india#lQsr")], "test/new-model", 1, 1, false,
            ["https://www.plonkit.net/india#lQsr"], ["https://www.plonkit.net/india#lQsr"]);

    private static AnswerRun Run(RatedQuestion rated, AiAnswer? answer) =>
        new(AnswerCache.KeyFor(rated.Record.FeedbackId, "settings"), rated.Record.FeedbackId, rated.Number, Now, answer,
            null, null,
            [new ChatCallRecord(["a/model"], 3, "auto", "a/model", "stop", 40, [], 100, 20, 2.5, null)],
            new AnswerCost(1, 1, 2, 100, 20), 4.2, "Guide excerpts:\n[1] Khasi pine", [], ["info AiAnswerLoop: answered"]);

    private static RatedQuestion Rated(int number, DateTimeOffset created)
    {
        var record = Record(("user", "q?", null), ("assistant", "a.", null)) with { CreatedAtUtc = created };
        return Parse(record, number);
    }

    private static RatedQuestion Parse(AiFeedbackRecord record, int number = 1) =>
        FeedbackExport.Parse(AiFeedbackFormatter.RenderJsonLines([record]).Split('\n')).Single() with { Number = number };

    private static AiFeedbackRecord Record(params (string Role, string Content, string? Image)[] turns) =>
        new(Guid.NewGuid(), "negative", "I would have liked an image.", 1, 2, 3, 4, 5, "test/model", turns.Length - 1,
            Now.AddDays(-20), Now.AddDays(-20),
            [.. turns.Select((turn, ordinal) => new AiFeedbackTurnRecord(
                ordinal, turn.Role, turn.Role == "assistant" ? 99UL : 42UL, turn.Content,
                turn.Image is { } image ? [image] : [],
                turn.Role == "assistant" ? "test/model" : null, Now.AddDays(-20)))],
            ["https://www.plonkit.net/india#lQsr", "https://docs/b"],
            ["https://www.plonkit.net/india#lQsr"]);

    private static string WriteSettings(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"probe-settings-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    private sealed class StubHandler(HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        private int _sent;

        public int Sent => _sent;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _sent);
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
