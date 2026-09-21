using Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using UseCases.UseCases.AI.Conversations;
using UseCases.UseCases.AI.Feedback;
using Xunit;

namespace GeoClubBot.Tests.Integration.UseCases;

/// <summary>
/// Drives the feedback use cases through the real MediatR pipeline against a real database.
///
/// The behaviour worth proving here is the one the whole design exists for: a rated conversation
/// survives the sweep that deletes every unrated one. That cannot be asserted against fakes — it is
/// a property of two independent delete paths over real tables.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AiFeedbackUseCaseIntegrationTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task Submit_ArchivesTheWholeBranch_OnTheFirstVerdict()
    {
        using var host = CreateHost();
        var branch = await SeedBranchAsync();
        var reviewer = NewSnowflake();

        var result = await host.SendAsync(Submit(branch.AnswerMessageId, reviewer, AiFeedbackRating.Negative, "wrong"));

        result.IsSuccess.Should().BeTrue();
        result.Value.WasCreated.Should().BeTrue();
        result.Value.RatingsOnMessage.Should().Be(1);

        var stored = await ReadFeedbackAsync(branch.AnswerMessageId);
        stored.Should().ContainSingle();
        stored[0].Comment.Should().Be("wrong");
        stored[0].ModelId.Should().Be("test/model");
        stored[0].ReviewerDiscordUserId.Should().Be(reviewer);
        stored[0].Turns.OrderBy(turn => turn.Ordinal).Select(turn => turn.Content)
            .Should().Equal("what country?", "Ghana.", "and the wires?", "Also Ghana.");
        stored[0].Turns.OrderBy(turn => turn.Ordinal).Last().RetrievedSourceUrls
            .Should().Equal("https://plonkit.net/ghana", "https://plonkit.net/togo");
    }

    [Fact]
    public async Task Submit_ArchivesAConversationThatOutlivesTheRetentionSweep()
    {
        // The point of the entire feature. Unrated conversations are deleted on a schedule because
        // storing what people asked is a privacy posture; a rated one has to stay behind.
        using var host = CreateHost();
        var branch = await SeedBranchAsync(createdAt: Now.AddDays(-90));

        await host.SendAsync(Submit(branch.AnswerMessageId, NewSnowflake(), AiFeedbackRating.Negative, null));

        var pruned = await host.SendAsync(new PruneAiConversationsCommand());
        pruned.IsSuccess.Should().BeTrue();

        await using var db = fixture.CreateDbContext();
        var survivingTurns = await db.AiConversationTurns
            .CountAsync(turn => turn.ConversationId == branch.ConversationId);
        survivingTurns.Should().Be(0, "the working store is swept regardless of feedback");

        var archived = await ReadFeedbackAsync(branch.AnswerMessageId);
        archived.Should().ContainSingle();
        archived[0].Turns.Should().HaveCount(4, "the archive is a copy, not a reference");
    }

    [Fact]
    public async Task Submit_UpdatesInPlace_WhenTheSameReviewerChangesTheirMind()
    {
        using var host = CreateHost();
        var branch = await SeedBranchAsync();
        var reviewer = NewSnowflake();

        await host.SendAsync(Submit(branch.AnswerMessageId, reviewer, AiFeedbackRating.Positive, "good"));
        var second = await host.SendAsync(Submit(branch.AnswerMessageId, reviewer, AiFeedbackRating.Negative, null));

        second.Value.WasCreated.Should().BeFalse();

        var stored = await ReadFeedbackAsync(branch.AnswerMessageId);
        stored.Should().ContainSingle("the unique index makes a second verdict a correction");
        stored[0].Rating.Should().Be(AiFeedbackRating.Negative);
        stored[0].Comment.Should().Be("good", "a reaction carries no comment and must not erase one");
        stored[0].Turns.Should().HaveCount(4, "the snapshot is reused rather than rebuilt");
    }

    [Fact]
    public async Task Submit_KeepsOneRowPerReviewer()
    {
        using var host = CreateHost();
        var branch = await SeedBranchAsync();

        await host.SendAsync(Submit(branch.AnswerMessageId, NewSnowflake(), AiFeedbackRating.Positive, null));
        var second = await host.SendAsync(Submit(branch.AnswerMessageId, NewSnowflake(), AiFeedbackRating.Negative, null));

        second.Value.RatingsOnMessage.Should().Be(2);
        (await ReadFeedbackAsync(branch.AnswerMessageId)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Submit_ResolvesAnEarlierChunkOfASplitAnswer()
    {
        // A long answer arrives as several messages and only the last is the stored turn. Reacting
        // on the first part is the natural thing to do, and must reach the same verdict row.
        using var host = CreateHost();
        var branch = await SeedBranchAsync();
        var reviewer = NewSnowflake();

        var viaChunk = await host.SendAsync(
            Submit(branch.EarlierChunkMessageId, reviewer, AiFeedbackRating.Negative, null));

        viaChunk.IsSuccess.Should().BeTrue();
        viaChunk.Value.RatedDiscordMessageId.Should().Be(branch.AnswerMessageId,
            "the archive names the answer, not whichever part was clicked");

        // Reacting again, this time on the answer's own message, must correct rather than duplicate.
        var viaAnswer = await host.SendAsync(
            Submit(branch.AnswerMessageId, reviewer, AiFeedbackRating.Positive, null));

        viaAnswer.Value.WasCreated.Should().BeFalse();
        (await ReadFeedbackAsync(branch.AnswerMessageId)).Should().ContainSingle();
    }

    [Fact]
    public async Task Submit_RefusesAQuestion()
    {
        using var host = CreateHost();
        var branch = await SeedBranchAsync();

        var result = await host.SendAsync(Submit(branch.QuestionMessageId, NewSnowflake(), AiFeedbackRating.Positive, null));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ai.feedback.not_an_answer");
    }

    [Fact]
    public async Task Submit_RefusesAnUnknownMessage()
    {
        using var host = CreateHost();

        var result = await host.SendAsync(Submit(NewSnowflake(), NewSnowflake(), AiFeedbackRating.Positive, null));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ai.feedback.not_an_answer");
    }

    [Fact]
    public async Task Submit_IsRefused_WhenFeedbackIsSwitchedOff()
    {
        using var host = CreateHost(feedbackEnabled: false);
        var branch = await SeedBranchAsync();

        var result = await host.SendAsync(Submit(branch.AnswerMessageId, NewSnowflake(), AiFeedbackRating.Positive, null));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ai.feedback.disabled");
    }

    [Fact]
    public async Task Withdraw_DeletesTheArchive_AndItsTranscript()
    {
        using var host = CreateHost();
        var branch = await SeedBranchAsync();
        var reviewer = NewSnowflake();

        await host.SendAsync(Submit(branch.AnswerMessageId, reviewer, AiFeedbackRating.Negative, "explained"));

        var result = await host.SendAsync(new WithdrawAiAnswerFeedbackCommand(
            branch.AnswerMessageId, reviewer, AiFeedbackRating.Negative));

        result.IsSuccess.Should().BeTrue();
        result.Value.RatingsOnMessage.Should().Be(0);

        (await ReadFeedbackAsync(branch.AnswerMessageId)).Should().BeEmpty();

        await using var db = fixture.CreateDbContext();
        var orphanedTurns = await db.AiFeedbackTurns
            .CountAsync(turn => turn.DiscordMessageId == branch.AnswerMessageId);
        orphanedTurns.Should().Be(0, "the transcript cascades with the verdict");
    }

    [Fact]
    public async Task Withdraw_IsANoOp_WhenTheRemovedReactionIsTheStaleOne()
    {
        // Both reactions can sit on the same message. Someone who reacted 👍 then 👎 has a stored
        // verdict of 👎; tidying away the 👍 must not delete the one they meant.
        using var host = CreateHost();
        var branch = await SeedBranchAsync();
        var reviewer = NewSnowflake();

        await host.SendAsync(Submit(branch.AnswerMessageId, reviewer, AiFeedbackRating.Positive, null));
        await host.SendAsync(Submit(branch.AnswerMessageId, reviewer, AiFeedbackRating.Negative, null));

        await host.SendAsync(new WithdrawAiAnswerFeedbackCommand(
            branch.AnswerMessageId, reviewer, AiFeedbackRating.Positive));

        var stored = await ReadFeedbackAsync(branch.AnswerMessageId);
        stored.Should().ContainSingle();
        stored[0].Rating.Should().Be(AiFeedbackRating.Negative);
    }

    [Fact]
    public async Task Withdraw_IsANoOp_ForAReactionThatWasNeverFeedback()
    {
        // Removing a reaction from an ordinary message is constant channel noise, not an error.
        using var host = CreateHost();

        var result = await host.SendAsync(new WithdrawAiAnswerFeedbackCommand(
            NewSnowflake(), NewSnowflake(), AiFeedbackRating.Positive));

        result.IsSuccess.Should().BeTrue();
        result.Value.RatingsOnMessage.Should().Be(0);
    }

    [Fact]
    public async Task Export_ReturnsTheTranscriptInOrder_WithTheRetrievalTrace()
    {
        using var host = CreateHost();
        var branch = await SeedBranchAsync();
        await host.SendAsync(Submit(branch.AnswerMessageId, NewSnowflake(), AiFeedbackRating.Negative, "wrong country"));

        var result = await host.SendAsync(new ExportAiFeedbackQuery(null, Days: null, Limit: null));

        var record = result.Value.Single(entry => entry.RatedDiscordMessageId == branch.AnswerMessageId);
        record.Rating.Should().Be("negative");
        record.Comment.Should().Be("wrong country");
        record.Transcript.Select(turn => turn.Ordinal).Should().Equal(0, 1, 2, 3);
        record.Transcript.Select(turn => turn.Role).Should().Equal("user", "assistant", "user", "assistant");
        record.RetrievedSourceUrls.Should().Equal("https://plonkit.net/ghana", "https://plonkit.net/togo");
        record.CitedSourceUrls.Should().Equal("https://plonkit.net/ghana");
    }

    [Fact]
    public async Task Export_HonoursTheConfiguredCeiling()
    {
        using var host = CreateHost(maxExportRecords: 1);

        var first = await SeedBranchAsync();
        var second = await SeedBranchAsync();
        await host.SendAsync(Submit(first.AnswerMessageId, NewSnowflake(), AiFeedbackRating.Positive, null));
        await host.SendAsync(Submit(second.AnswerMessageId, NewSnowflake(), AiFeedbackRating.Positive, null));

        var result = await host.SendAsync(new ExportAiFeedbackQuery(null, Days: null, Limit: 500));

        result.Value.Should().ContainSingle("the ceiling clamps whatever the caller asks for");
    }

    [Fact]
    public async Task Export_FiltersByVerdict()
    {
        using var host = CreateHost();
        var good = await SeedBranchAsync();
        var bad = await SeedBranchAsync();
        await host.SendAsync(Submit(good.AnswerMessageId, NewSnowflake(), AiFeedbackRating.Positive, null));
        await host.SendAsync(Submit(bad.AnswerMessageId, NewSnowflake(), AiFeedbackRating.Negative, null));

        var result = await host.SendAsync(new ExportAiFeedbackQuery(AiFeedbackRating.Negative, null, null));

        result.Value.Select(record => record.RatedDiscordMessageId).Should().Contain(bad.AnswerMessageId);
        result.Value.Select(record => record.RatedDiscordMessageId).Should().NotContain(good.AnswerMessageId);
    }

    [Fact]
    public async Task Summary_CountsVerdictsAndBreaksThemDownByModel()
    {
        using var host = CreateHost();
        var good = await SeedBranchAsync();
        var bad = await SeedBranchAsync();
        await host.SendAsync(Submit(good.AnswerMessageId, NewSnowflake(), AiFeedbackRating.Positive, null));
        await host.SendAsync(Submit(bad.AnswerMessageId, NewSnowflake(), AiFeedbackRating.Negative, "explained"));

        var result = await host.SendAsync(new ReadAiFeedbackSummaryQuery(Days: null));

        result.Value.Positive.Should().BeGreaterThanOrEqualTo(1);
        result.Value.Negative.Should().BeGreaterThanOrEqualTo(1);
        result.Value.WithComment.Should().BeGreaterThanOrEqualTo(1);
        result.Value.ByModel.Should().Contain(model => model.ModelId == "test/model");
    }

    [Fact]
    public async Task Prune_KeepsEverything_WhenRetentionIsUnset()
    {
        // Zero means forever, and it is the default: the archive is supposed to outlive the
        // conversation sweep, so an accidental window here would quietly defeat the feature.
        using var host = CreateHost();
        var branch = await SeedBranchAsync();
        await host.SendAsync(Submit(branch.AnswerMessageId, NewSnowflake(), AiFeedbackRating.Positive, null));

        var pruned = await host.SendAsync(new PruneAiFeedbackCommand());

        pruned.Value.Should().Be(0);
        (await ReadFeedbackAsync(branch.AnswerMessageId)).Should().ContainSingle();
    }

    [Fact]
    public async Task Prune_DeletesArchivedFeedbackPastItsOwnWindow()
    {
        using var host = CreateHost(feedbackRetentionDays: 1);
        var branch = await SeedBranchAsync();
        await host.SendAsync(Submit(branch.AnswerMessageId, NewSnowflake(), AiFeedbackRating.Positive, null));

        await BackdateFeedbackAsync(branch.AnswerMessageId, Now.AddDays(-10));

        var pruned = await host.SendAsync(new PruneAiFeedbackCommand());

        pruned.Value.Should().BeGreaterThanOrEqualTo(1);
        (await ReadFeedbackAsync(branch.AnswerMessageId)).Should().BeEmpty();
    }

    private MediatorTestHost CreateHost(
        bool feedbackEnabled = true,
        int feedbackRetentionDays = 0,
        int maxExportRecords = 2000) =>
        new(fixture.ConnectionString, configurationValues: new Dictionary<string, string?>
        {
            ["AI:Active"] = "true",
            ["AI:Conversation:RetentionDays"] = "30",
            ["AI:Feedback:Enabled"] = feedbackEnabled ? "true" : "false",
            ["AI:Feedback:RetentionDays"] = feedbackRetentionDays.ToString(),
            ["AI:Feedback:MaxExportRecords"] = maxExportRecords.ToString()
        });

    private static SubmitAiAnswerFeedbackCommand Submit(
        ulong messageId, ulong reviewer, AiFeedbackRating rating, string? comment) =>
        new(messageId, reviewer, rating, comment);

    /// <summary>
    /// Two exchanges written straight to the database, so the turns can be backdated — the record
    /// command always stamps them with "now", which the retention test has to get behind.
    /// </summary>
    private async Task<SeededBranch> SeedBranchAsync(DateTimeOffset? createdAt = null)
    {
        var at = createdAt ?? Now;

        var conversationId = NewSnowflake();
        var firstAnswerId = NewSnowflake();
        var followUpId = NewSnowflake();
        var answerId = NewSnowflake();
        var earlierChunkId = NewSnowflake();

        await using var db = fixture.CreateDbContext();

        db.AiConversationTurns.AddRange(
            AiConversationTurn.CreateUserTurn(conversationId, null, conversationId, ChannelId, GuildId,
                NewSnowflake(), "what country?", null, 0, at),
            AiConversationTurn.CreateAssistantTurn(firstAnswerId, conversationId, conversationId, ChannelId, GuildId,
                BotUserId, "Ghana.", "test/model", null, null, null, 1, at),
            AiConversationTurn.CreateUserTurn(followUpId, firstAnswerId, conversationId, ChannelId, GuildId,
                NewSnowflake(), "and the wires?", null, 2, at),
            AiConversationTurn.CreateAssistantTurn(answerId, followUpId, conversationId, ChannelId, GuildId,
                BotUserId, "Also Ghana.", "test/model",
                ["https://plonkit.net/ghana", "https://plonkit.net/togo"],
                ["https://plonkit.net/ghana"],
                [earlierChunkId], 3, at));

        await db.SaveChangesAsync();

        return new SeededBranch(conversationId, conversationId, answerId, earlierChunkId);
    }

    private async Task<List<AiAnswerFeedback>> ReadFeedbackAsync(ulong ratedMessageId)
    {
        await using var db = fixture.CreateDbContext();

        return await db.AiAnswerFeedbacks
            .AsNoTracking()
            .Include(feedback => feedback.Turns)
            .Where(feedback => feedback.RatedDiscordMessageId == ratedMessageId)
            .ToListAsync();
    }

    private async Task BackdateFeedbackAsync(ulong ratedMessageId, DateTimeOffset createdAt)
    {
        await using var db = fixture.CreateDbContext();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "AiAnswerFeedbacks" SET "CreatedAtUtc" = {createdAt} WHERE "RatedDiscordMessageId" = {(decimal)ratedMessageId}""");
    }

    /// <param name="EarlierChunkMessageId">A non-final part of the answer, as a split reply produces.</param>
    private sealed record SeededBranch(
        ulong ConversationId,
        ulong QuestionMessageId,
        ulong AnswerMessageId,
        ulong EarlierChunkMessageId);

    private const ulong ChannelId = 5;
    private const ulong GuildId = 7;
    private const ulong BotUserId = 1;

    /// <summary>A random Discord-shaped id, so tests never collide in the shared container.</summary>
    private static ulong NewSnowflake() => (ulong)Random.Shared.NextInt64(1_000_000_000, long.MaxValue);
}
