using Entities;
using FluentAssertions;
using Infrastructure.OutputAdapters.Repositories;
using Xunit;

namespace GeoClubBot.Tests.Integration.AI;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class EfAiRetrievalGapRepositoryIntegrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task ReadsBackRecentGaps_AndForgetsOldOnes()
    {
        // Far in the future, so other tests' rows in the shared container never fall in the window.
        var now = new DateTimeOffset(2200, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(Random.Shared.Next(0, 1_000_000));
        var marker = Guid.NewGuid().ToString("N");

        await using (var dbContext = fixture.CreateDbContext())
        {
            var repository = new EfAiRetrievalGapRepository(dbContext);
            repository.Add(AiRetrievalGap.Create(now - TimeSpan.FromDays(1), $"recent {marker}", "vietnam", "bollards", AiRetrievalGapReason.NothingCited));
            repository.Add(AiRetrievalGap.Create(now - TimeSpan.FromDays(200), $"old {marker}", null, null, AiRetrievalGapReason.NoExcerpts));
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            var repository = new EfAiRetrievalGapRepository(dbContext);

            var recent = (await repository.ReadSinceAsync(now - TimeSpan.FromDays(30))).Where(gap => gap.Question.EndsWith(marker)).ToList();
            recent.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Country = "vietnam", Topic = "bollards", Reason = AiRetrievalGapReason.NothingCited });

            (await repository.DeleteOlderThanAsync(now - TimeSpan.FromDays(90))).Should().BeGreaterThanOrEqualTo(1);
            (await repository.ReadSinceAsync(now - TimeSpan.FromDays(365))).Where(gap => gap.Question.EndsWith(marker)).Should().ContainSingle();
        }
    }
}
