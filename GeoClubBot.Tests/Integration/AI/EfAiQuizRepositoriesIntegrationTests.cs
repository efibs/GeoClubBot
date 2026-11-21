using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using Infrastructure.OutputAdapters.Repositories;
using UseCases.OutputPorts.Repositories;
using Xunit;

namespace GeoClubBot.Tests.Integration.AI;

/// <summary>
/// The quiz's ratings and rounds against a real PostgreSQL (#491): the draw's filters and order, and the
/// leaderboard's sums. Each test files its pictures under a country of its own and plays with member ids of
/// its own, so the shared container is reused safely.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class EfAiQuizRepositoriesIntegrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Draw_KeepsToKindDifficultyCountryAndGeneration_NeverAskedFirst()
    {
        var country = $"country-{Guid.NewGuid():N}";
        var asked = Item(country, AiQuizQuestionKind.Country, AiQuizDifficulty.Easy);
        var neverAsked = Item(country, AiQuizQuestionKind.Country, AiQuizDifficulty.Medium);
        var tooHard = Item(country, AiQuizQuestionKind.Country, AiQuizDifficulty.Hard);
        var regional = Item(country, AiQuizQuestionKind.Region, AiQuizDifficulty.Easy);
        var oldGeneration = Item(country, AiQuizQuestionKind.Country, AiQuizDifficulty.Easy, version: 0);
        var plonkIt = Item(country, AiQuizQuestionKind.Country, AiQuizDifficulty.Easy, sourceType: "plonkit");
        var elsewhere = Item($"country-{Guid.NewGuid():N}", AiQuizQuestionKind.Country, AiQuizDifficulty.Easy);

        await using (var dbContext = fixture.CreateDbContext())
        {
            var items = new EfAiQuizItemRepository(dbContext);
            foreach (var item in new[] { asked, neverAsked, tooHard, regional, oldGeneration, plonkIt, elsewhere })
            {
                await items.ReplaceAsync(item);
            }

            new EfAiQuizRoundRepository(dbContext).Add(RoundOf(asked.ChunkId));
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            var items = new EfAiQuizItemRepository(dbContext);

            var drawn = await items.DrawAsync(
                new QuizItemDraw(AiQuizQuestionKind.Country, [AiQuizDifficulty.Easy, AiQuizDifficulty.Medium], 1, [country], "plonkit"), 10);

            drawn.Select(item => item.ChunkId).Should().Equal(neverAsked.ChunkId, asked.ChunkId);
            drawn[0].Questions.Should().ContainSingle().Which.Decoys.Should().Equal("Latvia", "Lithuania", "Finland");

            var regionalDrawn = await items.DrawAsync(new QuizItemDraw(AiQuizQuestionKind.Region, [AiQuizDifficulty.Easy], 1, [country]), 10);
            regionalDrawn.Select(item => item.ChunkId).Should().Equal(regional.ChunkId);
        }
    }

    [Fact]
    public async Task Replace_KeepsOneRatingPerPicture_AndHashesAreReadByGeneration()
    {
        var country = $"country-{Guid.NewGuid():N}";
        var first = Item(country, AiQuizQuestionKind.Country, AiQuizDifficulty.Easy, version: 1);
        var again = AiQuizItem.Create(first.ChunkId, 2, "new-hash", country, null, "model", QuizRounds.AskedAt, []);

        await using (var dbContext = fixture.CreateDbContext())
        {
            await new EfAiQuizItemRepository(dbContext).ReplaceAsync(first);
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            await new EfAiQuizItemRepository(dbContext).ReplaceAsync(again);
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            var items = new EfAiQuizItemRepository(dbContext);
            (await items.ReadContentHashesAsync([first.ChunkId], 1)).Should().BeEmpty();
            (await items.ReadContentHashesAsync([first.ChunkId], 2)).Should().Equal(new Dictionary<Guid, string> { [first.ChunkId] = "new-hash" });

            await items.RemoveAsync([first.ChunkId]);
            (await items.ReadContentHashesAsync([first.ChunkId], 2)).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Rounds_KeepGuesses_DueRevealsAndScores()
    {
        var alice = (ulong)Random.Shared.NextInt64(1, long.MaxValue);
        var bob = (ulong)Random.Shared.NextInt64(1, long.MaxValue);
        var carol = (ulong)Random.Shared.NextInt64(1, long.MaxValue);
        var now = DateTimeOffset.UtcNow;

        var recentHard = QuizRounds.Round(difficulty: AiQuizDifficulty.Hard);
        var old = QuizRounds.Round(difficulty: AiQuizDifficulty.Easy);
        Guid recentId;

        await using (var dbContext = fixture.CreateDbContext())
        {
            var rounds = new EfAiQuizRoundRepository(dbContext);
            rounds.Add(recentHard);
            rounds.Add(old);
            recentHard.Guess(alice, recentHard.CorrectIndex, now);
            recentHard.Guess(bob, 0, now);
            old.Guess(bob, old.CorrectIndex, now.AddDays(-30));
            recentId = recentHard.Id;
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            var rounds = new EfAiQuizRoundRepository(dbContext);

            var read = await rounds.ReadAsync(recentId);
            read!.Guesses.Should().HaveCount(2);
            read.Options.Should().Equal("Latvia", "Estonia", "Lithuania", "Finland");

            read.Guess(alice, 0, now).Should().BeNull("one guess each");

            // A guess on a round read back, as the guess command makes it, is stored beside the others.
            read.Guess(carol, read.CorrectIndex, now).Should().NotBeNull();
            await dbContext.SaveChangesAsync();

            var allTime = await rounds.ReadScoresAsync(null);
            allTime.Single(line => line.UserId == alice).Should().Be(new QuizScoreLine(alice, 3, 1, 1));
            allTime.Single(line => line.UserId == bob).Should().Be(new QuizScoreLine(bob, 1, 1, 2));
            allTime.Single(line => line.UserId == carol).Should().Be(new QuizScoreLine(carol, 3, 1, 1));

            var recent = await rounds.ReadScoresAsync(now.AddDays(-7));
            recent.Single(line => line.UserId == bob).Should().Be(new QuizScoreLine(bob, 0, 0, 1));

            var due = await rounds.ReadDueAsync(QuizRounds.AskedAt.AddMinutes(11), 1000);
            due.Select(round => round.Id).Should().Contain(recentId);
            due.Single(round => round.Id == recentId).Reveal(now);
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            var due = await new EfAiQuizRoundRepository(dbContext).ReadDueAsync(QuizRounds.AskedAt.AddMinutes(11), 1000);
            due.Select(round => round.Id).Should().NotContain(recentId);
        }
    }

    private static AiQuizItem Item(string country, AiQuizQuestionKind kind, AiQuizDifficulty difficulty, int version = 1, string? sourceType = "guide") =>
        AiQuizItem.Create(Guid.NewGuid(), version, "hash", country, sourceType, "model", QuizRounds.AskedAt,
            [AiQuizItemQuestion.Create(kind, "Estonia", ["Latvia", "Lithuania", "Finland"], difficulty)]);

    private static AiQuizRound RoundOf(Guid chunkId)
    {
        var template = QuizRounds.Round();
        return AiQuizRound.Create(chunkId, template.Kind, template.Difficulty, template.Options, template.CorrectIndex, null, template.Guide,
            template.Section, template.Explanation, template.SourceUrl, template.ImageUrl, null, false, null, 100, 1, QuizRounds.AskedAt,
            TimeSpan.FromMinutes(10));
    }
}
