using Entities;
using FluentAssertions;
using Infrastructure.OutputAdapters.Repositories;
using Xunit;

namespace GeoClubBot.Tests.Integration.AI;

/// <summary>
/// The record of posted clues, against a real PostgreSQL: what the no-repeat rule reads back. Each test
/// uses its own channel id and chunk ids, so the shared container is reused safely.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class EfAiCluePostRepositoryIntegrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task ReadsBackTheChunksPostedSinceADate_AndNoOlderOnes()
    {
        var channel = (ulong)Random.Shared.NextInt64(1, long.MaxValue);
        var recent = Guid.NewGuid();
        var old = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using (var dbContext = fixture.CreateDbContext())
        {
            var repository = new EfAiCluePostRepository(dbContext);
            repository.Add(AiCluePost.Create(recent, "https://guide/recent", channel, 42, now - TimeSpan.FromDays(10)));
            repository.Add(AiCluePost.Create(old, "https://guide/old", channel, null, now - TimeSpan.FromDays(200)));
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            var repository = new EfAiCluePostRepository(dbContext);

            var since = await repository.ReadChunkIdsPostedSinceAsync(now - TimeSpan.FromDays(180));
            since.Should().Contain(recent).And.NotContain(old);

            var latest = await repository.ReadLatestAsync(1000);
            latest.Where(post => post.ChannelId == channel).Select(post => post.ChunkId).Should().Equal(recent, old);
            latest.Single(post => post.ChunkId == old).MessageId.Should().BeNull("a failed post is recorded all the same");
        }
    }
}
