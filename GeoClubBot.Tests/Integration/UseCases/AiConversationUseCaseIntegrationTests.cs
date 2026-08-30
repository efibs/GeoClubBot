using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Conversations;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Integration.UseCases;

/// <summary>
/// Drives the AI conversation use cases through the real MediatR pipeline against a real database.
///
/// The host auto-substitutes every Application interface under OutputPorts except those named
/// *Repository, so the provider-facing ports are fakes while conversation and budget storage are
/// genuine EF — exactly the split worth testing, since the budget and the reply tree are the parts
/// that must survive real persistence.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AiConversationUseCaseIntegrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task AskAi_AnswersAFreshQuestion_AndConsumesBudget()
    {
        using var host = CreateHost();
        ArrangeProviders(host, answer: "Ghanaian bollards are short and white.");

        var messageId = NewSnowflake();
        var result = await host.SendAsync(Ask(messageId, parentId: null, "what do Ghanaian bollards look like?"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Text.Should().Contain("Ghanaian");
        result.Value.ModelUsed.Should().Be("test/model");
        result.Value.ConversationId.Should().Be(messageId, "a fresh question roots its own conversation");
        result.Value.Depth.Should().Be(0);
    }

    [Fact]
    public async Task AskAi_ReplaysPriorTurns_WhenContinuingABranch()
    {
        using var host = CreateHost();
        var chat = ArrangeProviders(host, answer: "Follow-up answer.");

        var (userMessageId, botMessageId, conversationId) = await SeedExchangeAsync(
            host, "what country is this pole from?", "Looks Ghanaian.");

        await host.SendAsync(Ask(NewSnowflake(), parentId: botMessageId, "and the wires?"));

        var request = chat.ReceivedCalls()
            .Select(call => call.GetArguments()[0])
            .OfType<AiChatRequest>()
            .Last();

        var replayed = request.Messages.Select(m => m.ToPlainText()).ToList();
        replayed.Should().Contain(text => text.Contains("what country is this pole from?"));
        replayed.Should().Contain(text => text.Contains("Looks Ghanaian."));
        replayed[^1].Should().Contain("and the wires?");

        conversationId.Should().Be(userMessageId);
    }

    [Fact]
    public async Task AskAi_KeepsSiblingBranchesApart()
    {
        // The behaviour the whole tree design exists for, verified end to end rather than only in the
        // pure builder: two people replying to the same answer must not see each other's follow-ups.
        using var host = CreateHost();
        var chat = ArrangeProviders(host, answer: "answer");

        var (_, botMessageId, _) = await SeedExchangeAsync(host, "original question", "original answer");

        var userA = NewSnowflake();
        var firstBranch = await host.SendAsync(Ask(NewSnowflake(), botMessageId, "branch A question", authorId: userA));
        await RecordAsync(host, firstBranch.Value, botMessageId, "branch A question", userA);

        var userB = NewSnowflake();
        await host.SendAsync(Ask(NewSnowflake(), botMessageId, "branch B question", authorId: userB));

        var lastRequest = chat.ReceivedCalls()
            .Select(call => call.GetArguments()[0])
            .OfType<AiChatRequest>()
            .Last();

        var replayed = string.Join("\n", lastRequest.Messages.Select(m => m.ToPlainText()));
        replayed.Should().Contain("original question", "the shared prefix is common to both branches");
        replayed.Should().NotContain("branch A question", "the sibling branch must stay invisible");
    }

    [Fact]
    public async Task AskAi_RefusesOnceTheDailyBudgetIsSpent()
    {
        // The budget is deliberately global per UTC day, so every other test in this class has already
        // spent against today's row in the shared container. Reset it to isolate this assertion.
        await ResetTodaysBudgetAsync();

        // One question costs two upstream calls, so a budget of two allows exactly one question.
        using var host = CreateHost(dailyBudget: 2);
        ArrangeProviders(host, answer: "answer");

        (await host.SendAsync(Ask(NewSnowflake(), null, "first"))).IsSuccess.Should().BeTrue();

        var second = await host.SendAsync(Ask(NewSnowflake(), null, "second"));

        second.IsFailure.Should().BeTrue();
        second.Error.Code.Should().Be("ai.budget_exhausted");
    }

    [Fact]
    public async Task AskAi_ThrottlesASingleUser_WithoutBlockingEveryoneElse()
    {
        using var host = CreateHost(perUserPerHour: 1);
        ArrangeProviders(host, answer: "answer");

        var heavyUser = NewSnowflake();
        var first = await host.SendAsync(Ask(NewSnowflake(), null, "first", authorId: heavyUser));
        await RecordAsync(host, first.Value, parentId: null, "first", heavyUser);

        var throttled = await host.SendAsync(Ask(NewSnowflake(), null, "second", authorId: heavyUser));
        throttled.IsFailure.Should().BeTrue();
        throttled.Error.Code.Should().Be("ai.user_throttled");

        var otherUser = await host.SendAsync(Ask(NewSnowflake(), null, "mine", authorId: NewSnowflake()));
        otherUser.IsSuccess.Should().BeTrue("the cap is per user, not global");
    }

    [Fact]
    public async Task AskAi_RefusesRatherThanGuess_WhenRetrievalIsUnavailable()
    {
        // Observed in production: a transient embedder failure produced an empty hit list, the prompt
        // reported "No guide excerpts matched this question", and the model told the user in good
        // faith that the guides do not cover a road the guides document in detail. A retrievalless
        // answer that claims to have checked is worse than no answer, and it spends the chat request
        // to produce it, so the failure is surfaced instead.
        using var host = CreateHost();
        var chat = ArrangeProviders(host, answer: "From general knowledge.");
        host.Mock<IEmbedder>().EmbedAsync(Arg.Any<IReadOnlyList<EmbeddingInput>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<ReadOnlyMemory<float>>>.Failure(
                Error.Unexpected("ai.embedding_failed", "down")));

        var result = await host.SendAsync(Ask(NewSnowflake(), null, "question"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ai.embedding_failed");
        chat.ReceivedCalls().Should().BeEmpty("the chat request must not be spent on an ungrounded answer");
    }

    [Fact]
    public async Task AskAi_StillAnswers_WhenRetrievalSimplyFoundNothing()
    {
        // The other half of the distinction: a lookup that genuinely matched nothing is a real answer
        // about the corpus, and the model is told so plainly rather than left to invent guide content.
        using var host = CreateHost();
        var chat = ArrangeProviders(host, answer: "From general knowledge.");
        host.Mock<IKnowledgeIndex>().SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await host.SendAsync(Ask(NewSnowflake(), null, "question"));

        result.IsSuccess.Should().BeTrue();
        var request = chat.ReceivedCalls().Select(c => c.GetArguments()[0]).OfType<AiChatRequest>().Last();
        request.Messages[^1].ToPlainText().Should().Contain("No guide excerpts matched");
    }

    [Fact]
    public async Task AskAi_AsksForAVisionModel_OnlyWhenAnImageIsAttached()
    {
        using var host = CreateHost();
        ArrangeProviders(host, answer: "answer");
        var catalog = host.Mock<IChatModelCatalog>();

        await host.SendAsync(Ask(NewSnowflake(), null, "no image here"));
        await catalog.Received().ReadChainAsync(
            Arg.Is<ChatModelRequirements>(r => !r.NeedsImageInput), Arg.Any<CancellationToken>());

        await host.SendAsync(Ask(NewSnowflake(), null, "what is this?", images: ["https://cdn/x.png"]));
        await catalog.Received().ReadChainAsync(
            Arg.Is<ChatModelRequirements>(r => r.NeedsImageInput), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordAiTurns_LinksTheAnswerToTheQuestion()
    {
        using var host = CreateHost();
        ArrangeProviders(host, answer: "an answer");

        var userMessageId = NewSnowflake();
        var answer = await host.SendAsync(Ask(userMessageId, null, "a question"));
        var botMessageId = await RecordAsync(host, answer.Value, null, "a question", userId: 55, userMessageId);

        await using var db = fixture.CreateDbContext();
        var repository = new Infrastructure.OutputAdapters.Repositories.EfAiConversationRepository(db);

        var stored = await repository.ReadConversationAsync(answer.Value.ConversationId);
        stored.Should().HaveCount(2);

        var assistant = stored.Single(t => t.DiscordMessageId == botMessageId);
        assistant.ParentDiscordMessageId.Should().Be(userMessageId, "the answer replies to the question");
        assistant.Depth.Should().Be(1);
        assistant.ModelId.Should().Be("test/model");
    }

    private async Task ResetTodaysBudgetAsync()
    {
        await using var db = fixture.CreateDbContext();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""DELETE FROM "AiDailyBudgets" WHERE "DateUtc" = {DateOnly.FromDateTime(DateTime.UtcNow)}""");
    }

    private MediatorTestHost CreateHost(int dailyBudget = 1000, int perUserPerHour = 0) =>
        new(fixture.ConnectionString, configurationValues: new Dictionary<string, string?>
        {
            ["AI:Active"] = "true",
            ["AI:OpenRouter:DailyRequestBudget"] = dailyBudget.ToString(),
            ["AI:MaxRequestsPerUserPerHour"] = perUserPerHour.ToString(),
            ["AI:MaxImagesInReply"] = "3"
        });

    /// <summary>Scripts the faked provider ports so a question completes without touching the network.</summary>
    private static IChatModelClient ArrangeProviders(MediatorTestHost host, string answer)
    {
        host.Mock<IEmbedder>().EmbedAsync(Arg.Any<IReadOnlyList<EmbeddingInput>>(), Arg.Any<CancellationToken>())
            .Returns(call => Result<IReadOnlyList<ReadOnlyMemory<float>>>.Success(
                [.. ((IReadOnlyList<EmbeddingInput>)call[0]).Select(_ => new ReadOnlyMemory<float>(new float[4]))]));

        host.Mock<IKnowledgeIndex>().SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<KnowledgeHit>>([]));

        host.Mock<IChatModelCatalog>().ReadChainAsync(Arg.Any<ChatModelRequirements>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["test/model", "openrouter/free"]));

        var chat = host.Mock<IChatModelClient>();
        chat.CompleteAsync(Arg.Any<AiChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<AiChatResponse>.Success(new AiChatResponse(answer, "test/model", new AiTokenUsage(10, 5))));

        return chat;
    }

    /// <summary>Asks a question and stores the resulting exchange, as the gateway would.</summary>
    private async Task<(ulong UserMessageId, ulong BotMessageId, ulong ConversationId)> SeedExchangeAsync(
        MediatorTestHost host,
        string question,
        string answerText)
    {
        var chat = host.Mock<IChatModelClient>();
        chat.CompleteAsync(Arg.Any<AiChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<AiChatResponse>.Success(new AiChatResponse(answerText, "test/model", new AiTokenUsage(1, 1))));

        var userMessageId = NewSnowflake();
        var answer = await host.SendAsync(Ask(userMessageId, null, question));
        var botMessageId = await RecordAsync(host, answer.Value, null, question, userId: 55, userMessageId);

        return (userMessageId, botMessageId, answer.Value.ConversationId);
    }

    private static async Task<ulong> RecordAsync(
        MediatorTestHost host,
        AiAnswer answer,
        ulong? parentId,
        string question,
        ulong userId,
        ulong? userMessageId = null)
    {
        var botMessageId = NewSnowflake();

        await host.SendAsync(new RecordAiTurnsCommand(
            userMessageId ?? NewSnowflake(), parentId, botMessageId, answer.ConversationId,
            ChannelId: 5, GuildId: 7, userId, BotUserId: 1,
            question, [], answer.Text, answer.ModelUsed, answer.Depth));

        return botMessageId;
    }

    private static AskAiCommand Ask(
        ulong messageId,
        ulong? parentId,
        string content,
        ulong? authorId = null,
        IReadOnlyList<string>? images = null) =>
        new(messageId, parentId, ChannelId: 5, GuildId: 7, authorId ?? NewSnowflake(), content, images ?? []);

    private static ulong NewSnowflake() => (ulong)Random.Shared.NextInt64(1_000_000_000, long.MaxValue);
}
