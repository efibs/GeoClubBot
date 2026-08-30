using Entities;
using FluentAssertions;
using Infrastructure.OutputAdapters.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeoClubBot.Tests.Integration.AI;

/// <summary>
/// Exercises conversation storage against a real PostgreSQL instance, because the behaviour worth
/// proving lives in the schema: the unique message index, the array column, and the filtered deletes.
///
/// Each test namespaces its own data with random Discord snowflakes so the shared container is reused
/// safely.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class EfAiConversationRepositoryIntegrationTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AddTurn_RoundTripsEveryField_IncludingTheImageUrlArray()
    {
        var messageId = NewSnowflake();
        var authorId = NewSnowflake();

        await using (var db = fixture.CreateDbContext())
        {
            new EfAiConversationRepository(db).AddTurn(AiConversationTurn.CreateUserTurn(
                messageId, parentDiscordMessageId: null, conversationId: messageId,
                channelId: 5, guildId: 7, authorId, "what is this pole?",
                ["https://cdn.example/a.png", "https://cdn.example/b.png"], depth: 0, Now));

            await db.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext();
        var stored = await new EfAiConversationRepository(read).ReadByMessageIdAsync(messageId);

        stored.Should().NotBeNull();
        stored!.Content.Should().Be("what is this pole?");
        stored.Role.Should().Be(AiTurnRole.User);
        stored.AuthorDiscordUserId.Should().Be(authorId);
        stored.ParentDiscordMessageId.Should().BeNull();
        stored.ImageUrls.Should().Equal("https://cdn.example/a.png", "https://cdn.example/b.png");
    }

    [Fact]
    public async Task ReadConversation_ReturnsOnlyThatTree()
    {
        var mine = NewSnowflake();
        var theirs = NewSnowflake();

        await using (var db = fixture.CreateDbContext())
        {
            var repository = new EfAiConversationRepository(db);
            repository.AddTurn(UserTurn(mine, null, mine, "mine root", depth: 0));
            repository.AddTurn(Assistant(NewSnowflake(), mine, mine, "mine answer", depth: 1));
            repository.AddTurn(UserTurn(theirs, null, theirs, "other conversation", depth: 0));
            await db.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext();
        var turns = await new EfAiConversationRepository(read).ReadConversationAsync(mine);

        turns.Should().HaveCount(2);
        turns.Should().OnlyContain(turn => turn.ConversationId == mine);
    }

    [Fact]
    public async Task AddTurn_RejectsADuplicateDiscordMessage()
    {
        // The unique index is what stops a re-delivered gateway event from doubling a turn and
        // corrupting the reply tree.
        var messageId = NewSnowflake();

        await using (var db = fixture.CreateDbContext())
        {
            new EfAiConversationRepository(db).AddTurn(UserTurn(messageId, null, messageId, "first", depth: 0));
            await db.SaveChangesAsync();
        }

        await using var second = fixture.CreateDbContext();
        new EfAiConversationRepository(second).AddTurn(UserTurn(messageId, null, messageId, "duplicate", depth: 0));

        var act = async () => await second.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task CountUserTurnsSince_CountsOnlyThatUsersRecentQuestions()
    {
        var authorId = NewSnowflake();
        var otherAuthorId = NewSnowflake();
        var conversationId = NewSnowflake();

        await using (var db = fixture.CreateDbContext())
        {
            var repository = new EfAiConversationRepository(db);
            repository.AddTurn(UserTurn(NewSnowflake(), null, conversationId, "recent", depth: 0, authorId: authorId, createdAt: Now));
            repository.AddTurn(UserTurn(NewSnowflake(), null, conversationId, "also recent", depth: 0, authorId: authorId, createdAt: Now.AddMinutes(-10)));
            // Too old to count.
            repository.AddTurn(UserTurn(NewSnowflake(), null, conversationId, "old", depth: 0, authorId: authorId, createdAt: Now.AddHours(-5)));
            // Someone else's question.
            repository.AddTurn(UserTurn(NewSnowflake(), null, conversationId, "theirs", depth: 0, authorId: otherAuthorId, createdAt: Now));
            // Assistant turns are the bot's own output and must not count against a user's quota.
            repository.AddTurn(Assistant(NewSnowflake(), conversationId, conversationId, "answer", depth: 1, authorId: authorId, createdAt: Now));
            await db.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext();
        var count = await new EfAiConversationRepository(read)
            .CountUserTurnsSinceAsync(authorId, Now.AddHours(-1));

        count.Should().Be(2);
    }

    [Fact]
    public async Task DeleteOlderThan_RemovesExpiredHistoryOnly()
    {
        var conversationId = NewSnowflake();
        var keptId = NewSnowflake();

        await using (var db = fixture.CreateDbContext())
        {
            var repository = new EfAiConversationRepository(db);
            repository.AddTurn(UserTurn(NewSnowflake(), null, conversationId, "ancient", depth: 0, createdAt: Now.AddDays(-60)));
            repository.AddTurn(UserTurn(keptId, null, conversationId, "recent", depth: 0, createdAt: Now));
            await db.SaveChangesAsync();
        }

        await using var sweep = fixture.CreateDbContext();
        var removed = await new EfAiConversationRepository(sweep).DeleteOlderThanAsync(Now.AddDays(-30));

        removed.Should().Be(1);

        await using var read = fixture.CreateDbContext();
        var remaining = await new EfAiConversationRepository(read).ReadConversationAsync(conversationId);
        remaining.Should().ContainSingle().Which.DiscordMessageId.Should().Be(keptId);
    }

    private static AiConversationTurn UserTurn(
        ulong messageId,
        ulong? parentId,
        ulong conversationId,
        string content,
        int depth,
        ulong? authorId = null,
        DateTimeOffset? createdAt = null) =>
        AiConversationTurn.CreateUserTurn(messageId, parentId, conversationId, channelId: 5, guildId: 7,
            authorId ?? NewSnowflake(), content, null, depth, createdAt ?? Now);

    private static AiConversationTurn Assistant(
        ulong messageId,
        ulong parentId,
        ulong conversationId,
        string content,
        int depth,
        ulong? authorId = null,
        DateTimeOffset? createdAt = null) =>
        AiConversationTurn.CreateAssistantTurn(messageId, parentId, conversationId, channelId: 5, guildId: 7,
            authorId ?? 1, content, "test/model", depth, createdAt ?? Now);

    /// <summary>A random Discord-shaped id, so tests never collide in the shared container.</summary>
    private static ulong NewSnowflake() => (ulong)Random.Shared.NextInt64(1_000_000_000, long.MaxValue);
}
