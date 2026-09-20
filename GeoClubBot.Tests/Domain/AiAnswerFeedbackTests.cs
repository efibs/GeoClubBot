using Entities;
using FluentAssertions;
using Xunit;

namespace GeoClubBot.Tests.Domain;

/// <summary>
/// The archive's invariants. Every one of them exists because the export is read later, in bulk, by
/// someone who cannot go back and check what the original conversation looked like.
/// </summary>
public sealed class AiAnswerFeedbackTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    private const ulong Bot = 1;
    private const ulong Reviewer = 42;

    [Fact]
    public void Create_DerivesTheHeaderFromTheRatedAnswer()
    {
        var branch = Branch();

        var feedback = AiAnswerFeedback.Create(branch, Reviewer, AiFeedbackRating.Negative, "wrong country", Now);

        feedback.RatedDiscordMessageId.Should().Be(101);
        feedback.ConversationId.Should().Be(100);
        feedback.ChannelId.Should().Be(5);
        feedback.GuildId.Should().Be(7);
        feedback.ModelId.Should().Be("test/model");
        feedback.AnswerDepth.Should().Be(1);
        feedback.ReviewerDiscordUserId.Should().Be(Reviewer);
        feedback.Rating.Should().Be(AiFeedbackRating.Negative);
        feedback.Comment.Should().Be("wrong country");
    }

    [Fact]
    public void Create_CopiesTheBranchOldestFirst_WithContiguousOrdinals()
    {
        var feedback = AiAnswerFeedback.Create(Branch(), Reviewer, AiFeedbackRating.Positive, null, Now);

        feedback.Turns.Select(turn => turn.Ordinal).Should().Equal(0, 1);
        feedback.Turns.Select(turn => turn.Content).Should().Equal("what country?", "Ghana.");
        feedback.Turns.Select(turn => turn.Role).Should().Equal(AiTurnRole.User, AiTurnRole.Assistant);
        feedback.Turns.Should().OnlyContain(turn => turn.FeedbackId == feedback.FeedbackId);
    }

    [Fact]
    public void Create_KeepsTheOriginalTimestamps()
    {
        // The archive is read as a conversation. Stamping the copy with the moment somebody reacted
        // would put every turn of every exchange at the same instant.
        var asked = Now.AddDays(-3);
        var answered = asked.AddSeconds(4);

        var feedback = AiAnswerFeedback.Create(
            [User(100, null, "q", asked), Assistant(101, 100, "a", answered)],
            Reviewer, AiFeedbackRating.Positive, null, Now);

        feedback.Turns[0].CreatedAtUtc.Should().Be(asked);
        feedback.Turns[1].CreatedAtUtc.Should().Be(answered);
        feedback.CreatedAtUtc.Should().Be(Now);
    }

    [Fact]
    public void Create_CarriesTheRetrievalTraceOfEachTurn()
    {
        var feedback = AiAnswerFeedback.Create(Branch(), Reviewer, AiFeedbackRating.Negative, null, Now);

        feedback.Turns[^1].RetrievedSourceUrls.Should().Equal("https://plonkit.net/ghana", "https://plonkit.net/togo");
        feedback.Turns[^1].CitedSourceUrls.Should().Equal("https://plonkit.net/ghana");
    }

    [Fact]
    public void Create_RejectsAnEmptyBranch()
    {
        var create = () => AiAnswerFeedback.Create([], Reviewer, AiFeedbackRating.Positive, null, Now);

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_RejectsABranchEndingInAQuestion()
    {
        // You rate an answer, not a question. A user turn as the leaf means the caller resolved the
        // wrong message, and the export reads the last turn as the thing being judged.
        var create = () => AiAnswerFeedback.Create(
            [User(100, null, "q", Now)], Reviewer, AiFeedbackRating.Positive, null, Now);

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_ClampsAnOverlongComment_RatherThanRejectingIt()
    {
        var feedback = AiAnswerFeedback.Create(
            Branch(), Reviewer, AiFeedbackRating.Negative, new string('x', 5_000), Now);

        feedback.Comment.Should().HaveLength(1_000);
    }

    [Fact]
    public void Create_ClampsAnOverlongTurn_ToTheStoredColumnWidth()
    {
        var branch = new[] { User(100, null, "q", Now), Assistant(101, 100, new string('y', 12_000), Now) };

        var feedback = AiAnswerFeedback.Create(branch, Reviewer, AiFeedbackRating.Negative, null, Now);

        feedback.Turns[^1].Content.Should().HaveLength(8_000);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_TreatsBlankCommentsAsNoComment(string? comment)
    {
        var feedback = AiAnswerFeedback.Create(Branch(), Reviewer, AiFeedbackRating.Positive, comment, Now);

        feedback.Comment.Should().BeNull();
    }

    [Fact]
    public void ChangeRating_FlipsTheVerdictAndBumpsTheTimestamp_WithoutTouchingTheTranscript()
    {
        // Ancestors of a posted message never change, so the snapshot taken the first time is still
        // exactly what is being re-judged — rebuilding it would only risk it drifting.
        var feedback = AiAnswerFeedback.Create(Branch(), Reviewer, AiFeedbackRating.Positive, null, Now);
        var turnIds = feedback.Turns.Select(turn => turn.FeedbackTurnId).ToList();

        feedback.ChangeRating(AiFeedbackRating.Negative, Now.AddHours(1));

        feedback.Rating.Should().Be(AiFeedbackRating.Negative);
        feedback.UpdatedAtUtc.Should().Be(Now.AddHours(1));
        feedback.CreatedAtUtc.Should().Be(Now, "the archive still dates from when it was first rated");
        feedback.Turns.Select(turn => turn.FeedbackTurnId).Should().Equal(turnIds);
    }

    [Fact]
    public void AttachComment_ReplacesTheText()
    {
        var feedback = AiAnswerFeedback.Create(Branch(), Reviewer, AiFeedbackRating.Negative, "first", Now);

        feedback.AttachComment("second", Now.AddMinutes(5));

        feedback.Comment.Should().Be("second");
        feedback.UpdatedAtUtc.Should().Be(Now.AddMinutes(5));
    }

    private static AiConversationTurn[] Branch() =>
    [
        User(100, null, "what country?", Now.AddMinutes(-2)),
        Assistant(101, 100, "Ghana.", Now.AddMinutes(-1))
    ];

    private static AiConversationTurn User(ulong messageId, ulong? parentId, string content, DateTimeOffset at) =>
        AiConversationTurn.CreateUserTurn(messageId, parentId, conversationId: 100, channelId: 5, guildId: 7,
            authorDiscordUserId: 10, content, imageUrls: null, depth: 0, at);

    private static AiConversationTurn Assistant(ulong messageId, ulong parentId, string content, DateTimeOffset at) =>
        AiConversationTurn.CreateAssistantTurn(messageId, parentId, conversationId: 100, channelId: 5, guildId: 7,
            Bot, content, modelId: "test/model",
            retrievedSourceUrls: ["https://plonkit.net/ghana", "https://plonkit.net/togo"],
            citedSourceUrls: ["https://plonkit.net/ghana"],
            chunkMessageIds: null, depth: 1, at);
}
