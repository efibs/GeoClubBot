using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts.Repositories;

namespace Infrastructure.OutputAdapters.Repositories;

public class EfAiFeedbackRepository(GeoClubBotDbContext dbContext) : IAiFeedbackRepository
{
    /// <summary>Comments shown in the admin summary. Enough to spot a pattern, short of a wall of text.</summary>
    private const int RecentCommentLimit = 5;

    public async Task<AiAnswerFeedback?> ReadForUpdateAsync(
        ulong ratedDiscordMessageId,
        ulong reviewerDiscordUserId,
        CancellationToken cancellationToken = default) =>
        await dbContext.AiAnswerFeedbacks
            .Include(feedback => feedback.Turns)
            .FirstOrDefaultAsync(
                feedback => feedback.RatedDiscordMessageId == ratedDiscordMessageId
                            && feedback.ReviewerDiscordUserId == reviewerDiscordUserId,
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<int> CountForMessageAsync(
        ulong ratedDiscordMessageId,
        CancellationToken cancellationToken = default) =>
        await dbContext.AiAnswerFeedbacks
            .AsNoTracking()
            .CountAsync(feedback => feedback.RatedDiscordMessageId == ratedDiscordMessageId, cancellationToken)
            .ConfigureAwait(false);

    public void Add(AiAnswerFeedback feedback) => dbContext.AiAnswerFeedbacks.Add(feedback);

    public void Remove(AiAnswerFeedback feedback) => dbContext.AiAnswerFeedbacks.Remove(feedback);

    public async Task<IReadOnlyList<AiAnswerFeedback>> ReadForExportAsync(
        AiFeedbackRating? rating,
        DateTimeOffset? sinceUtc,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.AiAnswerFeedbacks.AsNoTracking().AsQueryable();

        if (rating is { } wanted)
        {
            query = query.Where(feedback => feedback.Rating == wanted);
        }

        if (sinceUtc is { } since)
        {
            query = query.Where(feedback => feedback.CreatedAtUtc >= since);
        }

        // Ordered on the server, then the transcripts ordered in memory: EF cannot order an Include
        // and a filtered include here would cost a second query per row.
        var entries = await query
            .Include(feedback => feedback.Turns)
            .OrderByDescending(feedback => feedback.CreatedAtUtc)
            .Take(Math.Max(1, limit))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var entry in entries)
        {
            entry.Turns.Sort((left, right) => left.Ordinal.CompareTo(right.Ordinal));
        }

        return entries;
    }

    public async Task<AiFeedbackCounts> ReadSummaryAsync(
        DateTimeOffset? sinceUtc,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.AiAnswerFeedbacks.AsNoTracking().AsQueryable();

        if (sinceUtc is { } since)
        {
            query = query.Where(feedback => feedback.CreatedAtUtc >= since);
        }

        var positive = await query
            .CountAsync(feedback => feedback.Rating == AiFeedbackRating.Positive, cancellationToken)
            .ConfigureAwait(false);

        var negative = await query
            .CountAsync(feedback => feedback.Rating == AiFeedbackRating.Negative, cancellationToken)
            .ConfigureAwait(false);

        var withComment = await query
            .CountAsync(feedback => feedback.Comment != null, cancellationToken)
            .ConfigureAwait(false);

        // Grouped by model *and* verdict, then folded in memory. The obvious shape — one group per
        // model with two conditional Counts — is not translatable, and pivoting a handful of rows
        // here is cheaper than the round trip that discovering it costs.
        var modelRows = await query
            .Where(feedback => feedback.ModelId != null)
            .GroupBy(feedback => new { feedback.ModelId, feedback.Rating })
            .Select(group => new { group.Key.ModelId, group.Key.Rating, Count = group.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byModel = modelRows
            .GroupBy(row => row.ModelId!, StringComparer.Ordinal)
            .Select(group => new AiFeedbackModelCount(
                group.Key,
                group.Where(row => row.Rating == AiFeedbackRating.Positive).Sum(row => row.Count),
                group.Where(row => row.Rating == AiFeedbackRating.Negative).Sum(row => row.Count)))
            .OrderByDescending(count => count.Negative)
            .ThenByDescending(count => count.Positive)
            .ToList();

        var recentComments = await query
            .Where(feedback => feedback.Comment != null)
            .OrderByDescending(feedback => feedback.CreatedAtUtc)
            .Take(RecentCommentLimit)
            .Select(feedback => new AiFeedbackCommentPreview(
                feedback.Rating, feedback.Comment!, feedback.ModelId, feedback.CreatedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new AiFeedbackCounts(positive, negative, withComment, byModel, recentComments);
    }

    public async Task<int> DeleteOlderThanAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken = default) =>
        // The transcript rows go with it through the cascade configured on the foreign key.
        await dbContext.AiAnswerFeedbacks
            .Where(feedback => feedback.CreatedAtUtc < cutoffUtc)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
}
