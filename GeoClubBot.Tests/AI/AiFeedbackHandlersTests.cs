using Configuration;
using Entities;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Feedback;
using UseCases.UseCases.AI.Tools;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.AI;

public sealed class AiFeedbackHandlersTests
{
    private const ulong QuestionId = 100;
    private const ulong AnswerId = 200;
    private const ulong SecondChunkOfTheAnswer = 201;
    private const ulong ConversationId = 300;
    private const ulong Reviewer = 42;
    private const ulong Bot = 7;

    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly IAiConversationRepository _conversations = Substitute.For<IAiConversationRepository>();
    private readonly IAiFeedbackRepository _feedbacks = Substitute.For<IAiFeedbackRepository>();
    private readonly AiFeedbackConfiguration _config = new();

    private static AiConversationTurn Question(string content = "Which country is this?") =>
        AiConversationTurn.CreateUserTurn(
            QuestionId, null, ConversationId, channelId: 5, guildId: 9, 11, content, ["https://img/q.png"], 0, Now);

    private static AiConversationTurn Answer(
        IEnumerable<Guid>? offered = null,
        IEnumerable<Guid>? cited = null,
        string? toolCallsJson = null) =>
        AiConversationTurn.CreateAssistantTurn(
            AnswerId, QuestionId, ConversationId, channelId: 5, guildId: 9, Bot, "Probably Germany.", "test/model",
            ["https://guide/a", "https://guide/b"], ["https://guide/a"], [SecondChunkOfTheAnswer], 1, Now.AddMinutes(1),
            offered, cited, toolCallsJson);

    private void AnswerIsStored(AiConversationTurn? answer = null) =>
        _conversations.ReadAssistantTurnByAnyMessageIdAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>())
            .Returns(answer ?? Answer());

    // ---- Submit -----------------------------------------------------------

    private Task<Result<AiFeedbackOutcome>> SubmitAsync(ulong messageId = AnswerId, AiFeedbackRating rating = AiFeedbackRating.Positive, string? comment = null) =>
        new SubmitAiAnswerFeedbackHandler(_conversations, _feedbacks, Options.Create(_config))
            .Handle(new SubmitAiAnswerFeedbackCommand(messageId, Reviewer, rating, comment), CancellationToken.None);

    [Fact]
    public async Task Submit_WhileSwitchedOff_IsRefused_BeforeLookingAnythingUp()
    {
        _config.Enabled = false;

        var result = await SubmitAsync();

        result.Error.Code.Should().Be("ai.feedback.disabled");
        result.Error.Type.Should().Be(ErrorType.Conflict);
        _conversations.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Submit_OnSomethingThatIsNoAnswer_IsNotFound()
    {
        _conversations.ReadAssistantTurnByAnyMessageIdAsync(AnswerId, Arg.Any<CancellationToken>()).Returns((AiConversationTurn?)null);

        var result = await SubmitAsync();

        result.Error.Code.Should().Be("ai.feedback.not_an_answer");
    }

    [Fact]
    public async Task Submit_ArchivesTheBranch_UnderTheAnswersOwnMessage()
    {
        AnswerIsStored();
        _conversations.ReadConversationAsync(ConversationId, Arg.Any<CancellationToken>()).Returns([Question(), Answer()]);
        _feedbacks.CountForMessageAsync(AnswerId, Arg.Any<CancellationToken>()).Returns(2);
        AiAnswerFeedback? archived = null;
        _feedbacks.When(f => f.Add(Arg.Any<AiAnswerFeedback>())).Do(call => archived = call.Arg<AiAnswerFeedback>());

        var result = await SubmitAsync(SecondChunkOfTheAnswer, AiFeedbackRating.Negative, "Wrong country");

        // Counts the verdict being added, which the unit of work has not committed yet.
        result.Value.Should().Be(new AiFeedbackOutcome(AnswerId, WasCreated: true, RatingsOnMessage: 3));
        archived!.RatedDiscordMessageId.Should().Be(AnswerId);
        archived.ReviewerDiscordUserId.Should().Be(Reviewer);
        archived.Rating.Should().Be(AiFeedbackRating.Negative);
        archived.Comment.Should().Be("Wrong country");
        archived.Turns.Should().HaveCount(2);
    }

    [Fact]
    public async Task Submit_OfATreeThatWasSweptAway_ArchivesNothing()
    {
        AnswerIsStored();
        _conversations.ReadConversationAsync(ConversationId, Arg.Any<CancellationToken>()).Returns([Question()]);

        var result = await SubmitAsync();

        result.Error.Code.Should().Be("ai.feedback.conversation_gone");
        _feedbacks.DidNotReceiveWithAnyArgs().Add(default!);
    }

    private AiAnswerFeedback ExistingVerdict(AiFeedbackRating rating, string? comment)
    {
        var existing = AiAnswerFeedback.Create([Question(), Answer()], Reviewer, rating, comment, Now);
        _feedbacks.ReadForUpdateAsync(AnswerId, Reviewer, Arg.Any<CancellationToken>()).Returns(existing);
        return existing;
    }

    [Fact]
    public async Task Submit_ASecondTime_AmendsTheVerdict_WithoutANewArchive()
    {
        AnswerIsStored();
        var existing = ExistingVerdict(AiFeedbackRating.Positive, "Typed earlier");
        _feedbacks.CountForMessageAsync(AnswerId, Arg.Any<CancellationToken>()).Returns(1);

        var result = await SubmitAsync(rating: AiFeedbackRating.Negative, comment: "Actually wrong");

        result.Value.Should().Be(new AiFeedbackOutcome(AnswerId, WasCreated: false, RatingsOnMessage: 1));
        existing.Rating.Should().Be(AiFeedbackRating.Negative);
        existing.Comment.Should().Be("Actually wrong");
        _feedbacks.DidNotReceiveWithAnyArgs().Add(default!);
        await _conversations.DidNotReceiveWithAnyArgs().ReadConversationAsync(default, default);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Submit_WithoutText_KeepsTheCommentTypedEarlier(string? comment)
    {
        AnswerIsStored();
        var existing = ExistingVerdict(AiFeedbackRating.Positive, "Typed earlier");

        await SubmitAsync(rating: AiFeedbackRating.Negative, comment: comment);

        existing.Rating.Should().Be(AiFeedbackRating.Negative);
        existing.Comment.Should().Be("Typed earlier");
    }

    // ---- Withdraw ---------------------------------------------------------

    private Task<Result<AiFeedbackOutcome>> WithdrawAsync(ulong messageId = AnswerId, AiFeedbackRating rating = AiFeedbackRating.Positive) =>
        new WithdrawAiAnswerFeedbackHandler(_conversations, _feedbacks)
            .Handle(new WithdrawAiAnswerFeedbackCommand(messageId, Reviewer, rating), CancellationToken.None);

    [Fact]
    public async Task Withdraw_DeletesTheVerdict_AndCountsItOut()
    {
        AnswerIsStored();
        var existing = ExistingVerdict(AiFeedbackRating.Positive, null);
        _feedbacks.CountForMessageAsync(AnswerId, Arg.Any<CancellationToken>()).Returns(3);

        var result = await WithdrawAsync(SecondChunkOfTheAnswer);

        result.Value.Should().Be(new AiFeedbackOutcome(AnswerId, WasCreated: false, RatingsOnMessage: 2));
        _feedbacks.Received(1).Remove(existing);
    }

    [Fact]
    public async Task Withdraw_NeverCountsBelowZero()
    {
        AnswerIsStored();
        ExistingVerdict(AiFeedbackRating.Positive, null);
        _feedbacks.CountForMessageAsync(AnswerId, Arg.Any<CancellationToken>()).Returns(0);

        var result = await WithdrawAsync();

        result.Value.RatingsOnMessage.Should().Be(0);
    }

    [Fact]
    public async Task Withdraw_OfAStaleReaction_KeepsTheVerdictTheyMeant()
    {
        AnswerIsStored();
        ExistingVerdict(AiFeedbackRating.Negative, null);
        _feedbacks.CountForMessageAsync(AnswerId, Arg.Any<CancellationToken>()).Returns(1);

        var result = await WithdrawAsync(rating: AiFeedbackRating.Positive);

        result.Value.RatingsOnMessage.Should().Be(1);
        _feedbacks.DidNotReceiveWithAnyArgs().Remove(default!);
    }

    [Fact]
    public async Task Withdraw_OfAnUnstoredReaction_IsOrdinaryChannelNoise()
    {
        _conversations.ReadAssistantTurnByAnyMessageIdAsync(555, Arg.Any<CancellationToken>()).Returns((AiConversationTurn?)null);
        _feedbacks.ReadForUpdateAsync(555, Reviewer, Arg.Any<CancellationToken>()).Returns((AiAnswerFeedback?)null);

        var result = await WithdrawAsync(555);

        result.Value.Should().Be(new AiFeedbackOutcome(555, WasCreated: false, RatingsOnMessage: 0));
        _feedbacks.DidNotReceiveWithAnyArgs().Remove(default!);
    }

    // ---- Summary ----------------------------------------------------------

    private readonly AiFeedbackCounts _counts = new(1, 2, 0, [], []);

    private Task<Result<AiFeedbackCounts>> SummaryAsync(int? days) =>
        new ReadAiFeedbackSummaryHandler(_feedbacks).Handle(new ReadAiFeedbackSummaryQuery(days), CancellationToken.None);

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task Summary_WithoutAWindow_CountsTheWholeArchive(int? days)
    {
        _feedbacks.ReadSummaryAsync(null, Arg.Any<CancellationToken>()).Returns(_counts);

        var result = await SummaryAsync(days);

        result.Value.Should().BeSameAs(_counts);
    }

    [Fact]
    public async Task Summary_WithAWindow_CountsFromThatManyDaysAgo()
    {
        _feedbacks.ReadSummaryAsync(Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>()).Returns(_counts);

        await SummaryAsync(7);

        var expected = DateTimeOffset.UtcNow.AddDays(-7);
        await _feedbacks.Received(1).ReadSummaryAsync(
            Arg.Is<DateTimeOffset?>(since => since > expected.AddMinutes(-1) && since <= expected),
            Arg.Any<CancellationToken>());
    }

    // ---- Prune ------------------------------------------------------------

    private Task<Result<int>> PruneAsync() =>
        new PruneAiFeedbackHandler(_feedbacks, Options.Create(_config)).Handle(new PruneAiFeedbackCommand(), CancellationToken.None);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Prune_KeepsEverything_ByDefault(int retentionDays)
    {
        _config.RetentionDays = retentionDays;

        var result = await PruneAsync();

        result.Value.Should().Be(0);
        await _feedbacks.DidNotReceiveWithAnyArgs().DeleteOlderThanAsync(default, default);
    }

    [Fact]
    public async Task Prune_DeletesWhatIsPastItsRetention()
    {
        _config.RetentionDays = 30;
        _feedbacks.DeleteOlderThanAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(4);

        var result = await PruneAsync();

        result.Value.Should().Be(4);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        await _feedbacks.Received(1).DeleteOlderThanAsync(
            Arg.Is<DateTimeOffset>(d => d > cutoff.AddMinutes(-1) && d <= cutoff),
            Arg.Any<CancellationToken>());
    }

    // ---- Export -----------------------------------------------------------

    private int? _exportLimit;
    private DateTimeOffset? _exportSince;

    private async Task<IReadOnlyList<AiFeedbackRecord>> ExportAsync(
        AiFeedbackRating? rating = null,
        int? days = null,
        int? limit = null,
        params AiAnswerFeedback[] stored)
    {
        _feedbacks.ReadForExportAsync(Arg.Any<AiFeedbackRating?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _exportSince = call.ArgAt<DateTimeOffset?>(1);
                _exportLimit = call.ArgAt<int>(2);
                return stored;
            });

        var result = await new ExportAiFeedbackHandler(_feedbacks, Options.Create(_config))
            .Handle(new ExportAiFeedbackQuery(rating, days, limit), CancellationToken.None);
        return result.Value;
    }

    [Theory]
    [InlineData(null, 100)]
    [InlineData(0, 100)]
    [InlineData(-5, 100)]
    [InlineData(40, 40)]
    [InlineData(500, 100)]
    public async Task Export_IsCappedByTheConfiguration(int? requested, int expected)
    {
        _config.MaxExportRecords = 100;

        await ExportAsync(limit: requested);

        _exportLimit.Should().Be(expected);
    }

    [Fact]
    public async Task Export_AlwaysAllowsAtLeastOneRecord()
    {
        _config.MaxExportRecords = 0;

        await ExportAsync();

        _exportLimit.Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-2)]
    public async Task Export_WithoutAWindow_ReadsEverything(int? days)
    {
        await ExportAsync(days: days);

        _exportSince.Should().BeNull();
    }

    [Fact]
    public async Task Export_WithAWindow_ReadsFromThatManyDaysAgo()
    {
        await ExportAsync(days: 7);

        _exportSince.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(-7), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Export_FiltersByRating()
    {
        await ExportAsync(AiFeedbackRating.Negative);

        await _feedbacks.Received(1).ReadForExportAsync(
            AiFeedbackRating.Negative, Arg.Any<DateTimeOffset?>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Export_FlattensTheVerdict()
    {
        var feedback = AiAnswerFeedback.Create([Question(), Answer()], Reviewer, AiFeedbackRating.Negative, "Wrong", Now);

        var record = (await ExportAsync(stored: feedback)).Should().ContainSingle().Subject;

        record.FeedbackId.Should().Be(feedback.FeedbackId);
        record.Rating.Should().Be("negative");
        record.Comment.Should().Be("Wrong");
        record.ReviewerDiscordUserId.Should().Be(Reviewer);
        record.RatedDiscordMessageId.Should().Be(AnswerId);
        record.ConversationId.Should().Be(ConversationId);
        record.ChannelId.Should().Be(5);
        record.GuildId.Should().Be(9);
        record.ModelId.Should().Be("test/model");
        record.AnswerDepth.Should().Be(1);
        record.CreatedAtUtc.Should().Be(Now);
        record.UpdatedAtUtc.Should().Be(Now);
    }

    [Fact]
    public async Task Export_ListsTheTranscriptInOrder_WithRolesSpelledForChatTooling()
    {
        var feedback = AiAnswerFeedback.Create([Question(), Answer()], Reviewer, AiFeedbackRating.Positive, null, Now);
        feedback.Turns.Reverse();

        var record = (await ExportAsync(stored: feedback)).Single();

        record.Transcript.Select(t => t.Role).Should().Equal("user", "assistant");
        record.Transcript[0].Content.Should().Be("Which country is this?");
        record.Transcript[0].ImageUrls.Should().Equal("https://img/q.png");
        record.Transcript[1].ModelId.Should().Be("test/model");
    }

    [Fact]
    public async Task Export_TakesTheSourcesFromTheRatedAnswer_EvenWhenTurnsArriveShuffled()
    {
        var feedback = AiAnswerFeedback.Create([Question(), Answer()], Reviewer, AiFeedbackRating.Positive, null, Now);
        feedback.Turns.Reverse();

        var record = (await ExportAsync(stored: feedback)).Single();

        record.RetrievedSourceUrls.Should().Equal("https://guide/a", "https://guide/b");
        record.CitedSourceUrls.Should().Equal("https://guide/a");
    }

    [Fact]
    public async Task Export_OfATranscriptlessEntry_HasNoSources()
    {
        var feedback = AiAnswerFeedback.Create([Question(), Answer()], Reviewer, AiFeedbackRating.Positive, null, Now);
        feedback.Turns.Clear();

        var record = (await ExportAsync(stored: feedback)).Single();

        record.Transcript.Should().BeEmpty();
        record.RetrievedSourceUrls.Should().BeEmpty();
        record.CitedSourceUrls.Should().BeEmpty();
    }

    [Fact]
    public async Task Export_CarriesAnAnswersEvidence_ButLeavesItOffAQuestion()
    {
        var offered = Guid.NewGuid();
        var cited = Guid.NewGuid();
        var toolCalls = AiToolCallLog.Serialize([new AiToolCallSummary(1, "search_guides", "{}", AiToolCallOutcome.Answered, 3, 12, 5)]);
        var feedback = AiAnswerFeedback.Create(
            [Question(), Answer([offered], [cited], toolCalls)], Reviewer, AiFeedbackRating.Positive, null, Now);

        var transcript = (await ExportAsync(stored: feedback)).Single().Transcript;

        transcript[0].OfferedChunkIds.Should().BeNull();
        transcript[0].ToolCalls.Should().BeNull();
        transcript[0].CitedSourceUrls.Should().BeNull();
        transcript[1].OfferedChunkIds.Should().Equal(offered);
        transcript[1].CitedChunkIds.Should().Equal(cited);
        transcript[1].ToolCalls.Should().ContainSingle().Which.Tool.Should().Be("search_guides");
        transcript[1].CitedSourceUrls.Should().Equal("https://guide/a");
    }

    [Fact]
    public async Task Export_LeavesToolCallsOut_WhenTheAnswerMadeNone()
    {
        var feedback = AiAnswerFeedback.Create([Question(), Answer()], Reviewer, AiFeedbackRating.Positive, null, Now);

        var transcript = (await ExportAsync(stored: feedback)).Single().Transcript;

        transcript[1].ToolCalls.Should().BeNull();
    }
}
