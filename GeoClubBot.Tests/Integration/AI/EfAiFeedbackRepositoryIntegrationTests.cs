using Entities;
using FluentAssertions;
using Infrastructure.OutputAdapters.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeoClubBot.Tests.Integration.AI;

/// <summary>
/// The archive against real Postgres. The constraints asserted here exist only in the database —
/// the unique index that turns a second verdict into a correction, and the cascade that keeps a
/// transcript from outliving the verdict it was stored for.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class EfAiFeedbackRepositoryIntegrationTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task Add_RoundTripsTheTranscriptAndItsArrayColumns()
    {
        var answerId = NewSnowflake();
        var reviewer = NewSnowflake();

        await AddAsync(Feedback(answerId, reviewer, AiFeedbackRating.Negative, "wrong"));

        await using var db = fixture.CreateDbContext();
        var repository = new EfAiFeedbackRepository(db);

        var stored = await repository.ReadForUpdateAsync(answerId, reviewer);

        stored.Should().NotBeNull();
        stored!.Comment.Should().Be("wrong");
        stored.Turns.Should().HaveCount(2);

        var answerTurn = stored.Turns.Single(turn => turn.Role == AiTurnRole.Assistant);
        answerTurn.ImageUrls.Should().BeEmpty();
        answerTurn.RetrievedSourceUrls.Should().Equal("https://plonkit.net/ghana", "https://plonkit.net/togo");
        answerTurn.CitedSourceUrls.Should().Equal("https://plonkit.net/ghana");

        var questionTurn = stored.Turns.Single(turn => turn.Role == AiTurnRole.User);
        questionTurn.ImageUrls.Should().Equal("https://cdn.discordapp.com/pole.png");
    }

    [Fact]
    public async Task Add_RejectsASecondVerdictFromTheSameReviewer()
    {
        // The rule that makes a re-reaction a correction rather than a duplicate. Enforced in the
        // database because two reactions can land concurrently, past any check in the handler.
        var answerId = NewSnowflake();
        var reviewer = NewSnowflake();

        await AddAsync(Feedback(answerId, reviewer, AiFeedbackRating.Positive, null));

        var duplicate = async () => await AddAsync(Feedback(answerId, reviewer, AiFeedbackRating.Negative, null));

        await duplicate.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Add_AllowsSeveralReviewersOnOneAnswer()
    {
        var answerId = NewSnowflake();

        await AddAsync(Feedback(answerId, NewSnowflake(), AiFeedbackRating.Positive, null));
        await AddAsync(Feedback(answerId, NewSnowflake(), AiFeedbackRating.Negative, null));

        await using var db = fixture.CreateDbContext();
        var repository = new EfAiFeedbackRepository(db);

        (await repository.CountForMessageAsync(answerId)).Should().Be(2);
    }

    [Fact]
    public async Task Remove_CascadesToTheTranscript()
    {
        var answerId = NewSnowflake();
        var reviewer = NewSnowflake();
        await AddAsync(Feedback(answerId, reviewer, AiFeedbackRating.Negative, null));

        await using (var db = fixture.CreateDbContext())
        {
            var repository = new EfAiFeedbackRepository(db);
            var stored = await repository.ReadForUpdateAsync(answerId, reviewer);
            repository.Remove(stored!);
            await db.SaveChangesAsync();
        }

        await using var check = fixture.CreateDbContext();
        (await check.AiFeedbackTurns.CountAsync(turn => turn.DiscordMessageId == answerId)).Should().Be(0);
    }

    [Fact]
    public async Task ReadForExport_ReturnsNewestFirst_WithTranscriptsInOrdinalOrder()
    {
        var older = NewSnowflake();
        var newer = NewSnowflake();
        await AddAsync(Feedback(older, NewSnowflake(), AiFeedbackRating.Positive, null, Now.AddHours(-2)));
        await AddAsync(Feedback(newer, NewSnowflake(), AiFeedbackRating.Negative, null, Now));

        await using var db = fixture.CreateDbContext();
        var repository = new EfAiFeedbackRepository(db);

        var exported = await repository.ReadForExportAsync(null, Now.AddHours(-3), limit: 100);

        var ids = exported.Select(entry => entry.RatedDiscordMessageId).ToList();
        ids.IndexOf(newer).Should().BeLessThan(ids.IndexOf(older));
        exported.Should().OnlyContain(entry => entry.Turns[0].Ordinal == 0);
    }

    [Fact]
    public async Task DeleteOlderThan_RemovesExpiredArchiveOnly()
    {
        var expired = NewSnowflake();
        var kept = NewSnowflake();
        await AddAsync(Feedback(expired, NewSnowflake(), AiFeedbackRating.Positive, null, Now.AddDays(-40)));
        await AddAsync(Feedback(kept, NewSnowflake(), AiFeedbackRating.Positive, null, Now));

        await using var db = fixture.CreateDbContext();
        var repository = new EfAiFeedbackRepository(db);

        await repository.DeleteOlderThanAsync(Now.AddDays(-30));

        (await repository.CountForMessageAsync(expired)).Should().Be(0);
        (await repository.CountForMessageAsync(kept)).Should().Be(1);
    }

    private async Task AddAsync(AiAnswerFeedback feedback)
    {
        await using var db = fixture.CreateDbContext();
        new EfAiFeedbackRepository(db).Add(feedback);
        await db.SaveChangesAsync();
    }

    private static AiAnswerFeedback Feedback(
        ulong answerMessageId,
        ulong reviewer,
        AiFeedbackRating rating,
        string? comment,
        DateTimeOffset? at = null)
    {
        var conversationId = NewSnowflake();
        var now = at ?? Now;

        var question = AiConversationTurn.CreateUserTurn(
            conversationId, null, conversationId, ChannelId, GuildId, NewSnowflake(),
            "what country?", ["https://cdn.discordapp.com/pole.png"], 0, now);

        var answer = AiConversationTurn.CreateAssistantTurn(
            answerMessageId, conversationId, conversationId, ChannelId, GuildId, BotUserId,
            "Ghana.", "test/model",
            ["https://plonkit.net/ghana", "https://plonkit.net/togo"],
            ["https://plonkit.net/ghana"],
            chunkMessageIds: null, 1, now);

        return AiAnswerFeedback.Create([question, answer], reviewer, rating, comment, now);
    }

    private const ulong ChannelId = 5;
    private const ulong GuildId = 7;
    private const ulong BotUserId = 1;

    /// <summary>A random Discord-shaped id, so tests never collide in the shared container.</summary>
    private static ulong NewSnowflake() => (ulong)Random.Shared.NextInt64(1_000_000_000, long.MaxValue);
}
