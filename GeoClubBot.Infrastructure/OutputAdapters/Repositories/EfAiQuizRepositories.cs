using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts.Repositories;

namespace Infrastructure.OutputAdapters.Repositories;

public class EfAiQuizItemRepository(GeoClubBotDbContext dbContext) : IAiQuizItemRepository
{
    public async Task<IReadOnlyList<AiQuizItem>> DrawAsync(QuizItemDraw draw, int count, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draw);

        var difficulties = draw.Difficulties.ToList();
        var questions = dbContext.AiQuizItemQuestions
            .Where(question => question.Kind == draw.Kind && difficulties.Contains(question.Difficulty))
            .Select(question => question.ChunkId);

        var query = dbContext.AiQuizItems
            .AsNoTracking()
            .Where(item => item.Version == draw.Version && questions.Contains(item.ChunkId));

        if (draw.Countries is { Count: > 0 } countries)
        {
            var spellings = countries.ToList();
            query = query.Where(item => item.Country != null && spellings.Contains(item.Country));
        }

        if (draw.ExcludedSourceType is { } excluded)
        {
            query = query.Where(item => item.SourceType == null || item.SourceType != excluded);
        }

        // Never asked first, then asked longest ago; at random among equals.
        var chunkIds = await query
            .Select(item => new
            {
                item.ChunkId,
                LastAsked = dbContext.AiQuizRounds
                    .Where(round => round.ChunkId == item.ChunkId)
                    .Max(round => (DateTimeOffset?)round.AskedAtUtc)
            })
            .OrderBy(item => item.LastAsked != null)
            .ThenBy(item => item.LastAsked)
            .ThenBy(_ => EF.Functions.Random())
            .Select(item => item.ChunkId)
            .Take(Math.Max(1, count))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (chunkIds.Count == 0)
        {
            return [];
        }

        var items = await dbContext.AiQuizItems
            .AsNoTracking()
            .Include(item => item.Questions)
            .Where(item => chunkIds.Contains(item.ChunkId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. chunkIds.Select(id => items.First(item => item.ChunkId == id))];
    }

    public async Task<IReadOnlyDictionary<Guid, string>> ReadContentHashesAsync(
        IReadOnlyCollection<Guid> chunkIds,
        int version,
        CancellationToken cancellationToken = default)
    {
        if (chunkIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var ids = chunkIds.ToList();
        return await dbContext.AiQuizItems
            .AsNoTracking()
            .Where(item => item.Version == version && ids.Contains(item.ChunkId))
            .ToDictionaryAsync(item => item.ChunkId, item => item.ContentHash, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ReplaceAsync(AiQuizItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        // At once rather than with the unit of work, so the new rating's insert never meets the old row.
        await dbContext.AiQuizItems
            .Where(existing => existing.ChunkId == item.ChunkId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        dbContext.AiQuizItems.Add(item);
    }

    public async Task RemoveAsync(IReadOnlyCollection<Guid> chunkIds, CancellationToken cancellationToken = default)
    {
        if (chunkIds.Count == 0)
        {
            return;
        }

        var ids = chunkIds.ToList();
        await dbContext.AiQuizItems
            .Where(item => ids.Contains(item.ChunkId))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<(int Rated, int WithQuestions)> CountAsync(int version, CancellationToken cancellationToken = default)
    {
        var rated = await dbContext.AiQuizItems.CountAsync(item => item.Version == version, cancellationToken).ConfigureAwait(false);
        var withQuestions = await dbContext.AiQuizItems
            .CountAsync(item => item.Version == version && item.Questions.Count > 0, cancellationToken)
            .ConfigureAwait(false);
        return (rated, withQuestions);
    }
}

public class EfAiQuizRoundRepository(GeoClubBotDbContext dbContext) : IAiQuizRoundRepository
{
    public void Add(AiQuizRound round) => dbContext.AiQuizRounds.Add(round);

    public Task<AiQuizRound?> ReadAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.AiQuizRounds
            .Include(round => round.Guesses)
            .FirstOrDefaultAsync(round => round.Id == id, cancellationToken);

    public async Task<IReadOnlyList<AiQuizRound>> ReadDueAsync(DateTimeOffset nowUtc, int max, CancellationToken cancellationToken = default) =>
        await dbContext.AiQuizRounds
            .Include(round => round.Guesses)
            .Where(round => round.RevealedAtUtc == null && round.RevealAtUtc <= nowUtc)
            .OrderBy(round => round.RevealAtUtc)
            .Take(Math.Max(1, max))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<QuizScoreLine>> ReadScoresAsync(DateTimeOffset? sinceUtc, CancellationToken cancellationToken = default)
    {
        var guesses = dbContext.AiQuizGuesses.AsNoTracking();
        if (sinceUtc is { } since)
        {
            guesses = guesses.Where(guess => guess.AnsweredAtUtc >= since);
        }

        var lines = await guesses
            .GroupBy(guess => guess.UserId)
            .Select(group => new
            {
                UserId = group.Key,
                Points = group.Sum(guess => guess.Points),
                Right = group.Count(guess => guess.Correct),
                Answered = group.Count()
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. lines
                .Select(line => new QuizScoreLine(line.UserId, line.Points, line.Right, line.Answered))
                .OrderByDescending(line => line.Points)
                .ThenByDescending(line => line.Right)
                .ThenBy(line => line.Answered)
                .ThenBy(line => line.UserId)
        ];
    }
}
