using FluentAssertions;
using Infrastructure.OutputAdapters.DataAccess;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using NSubstitute;
using Xunit;

namespace GeoClubBot.Tests.Integration.AI;

/// <summary>
/// The migration that adds the excerpt ids and tool calls to stored turns, run over tables that already
/// hold rows — as production's do. Every other test migrates an empty database, where a NOT NULL column
/// without a default is added without complaint; on a populated table it fails the start-up.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AiTurnExcerptIdsMigrationIntegrationTests(PostgresFixture fixture)
{
    private const string MigrationBefore = "20261023202038_AddMissionClaims";

    [Fact]
    public async Task BackfillsTurnsStoredBeforeIt_AsNamingNoExcerpts()
    {
        await using var db = await CreateDatabaseAsync();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBefore);

        var turnId = Guid.NewGuid();
        var feedbackId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AiConversationTurns"
                ("TurnId", "ConversationId", "DiscordMessageId", "ChannelId", "AuthorDiscordUserId", "Role", "Content",
                 "ImageUrls", "RetrievedSourceUrls", "CitedSourceUrls", "ChunkMessageIds", "Depth", "CreatedAtUtc")
            VALUES ({turnId}, 1, 2, 3, 4, 'Assistant', 'An answer [1].', ARRAY[]::text[], ARRAY[]::text[], ARRAY[]::text[],
                    ARRAY[]::numeric(20,0)[], 1, {DateTimeOffset.UtcNow})
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AiAnswerFeedbacks"
                ("FeedbackId", "RatedDiscordMessageId", "ConversationId", "ChannelId", "ReviewerDiscordUserId", "Rating",
                 "AnswerDepth", "CreatedAtUtc", "UpdatedAtUtc")
            VALUES ({feedbackId}, 2, 1, 3, 5, 'Negative', 1, {DateTimeOffset.UtcNow}, {DateTimeOffset.UtcNow})
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AiFeedbackTurns"
                ("FeedbackTurnId", "FeedbackId", "Ordinal", "Role", "AuthorDiscordUserId", "DiscordMessageId", "Content",
                 "ImageUrls", "RetrievedSourceUrls", "CitedSourceUrls", "CreatedAtUtc")
            VALUES ({Guid.NewGuid()}, {feedbackId}, 0, 'Assistant', 4, 2, 'An answer [1].', ARRAY[]::text[], ARRAY[]::text[],
                    ARRAY[]::text[], {DateTimeOffset.UtcNow})
            """);

        await migrator.MigrateAsync();

        var turn = await db.AiConversationTurns.AsNoTracking().SingleAsync(stored => stored.TurnId == turnId);
        turn.OfferedChunkIds.Should().BeEmpty();
        turn.CitedChunkIds.Should().BeEmpty();
        turn.ToolCallsJson.Should().BeNull();

        var archived = await db.AiAnswerFeedbacks.AsNoTracking().Include(feedback => feedback.Turns)
            .SingleAsync(feedback => feedback.FeedbackId == feedbackId);
        archived.Turns.Should().ContainSingle().Which.CitedChunkIds.Should().BeEmpty();
    }

    /// <summary>A database of its own in the shared container, so migrating backwards touches no other test's.</summary>
    private async Task<GeoClubBotDbContext> CreateDatabaseAsync()
    {
        var name = $"migration_{Guid.NewGuid():N}";

        await using (var admin = new NpgsqlConnection(fixture.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = name }.ConnectionString;
        var options = new DbContextOptionsBuilder<GeoClubBotDbContext>().UseNpgsql(connection).Options;
        return new GeoClubBotDbContext(options, Substitute.For<IMediator>());
    }
}
