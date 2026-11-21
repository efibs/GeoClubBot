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

        // One group per model with two conditional Counts, which Npgsql translates to
        // count(*) FILTER (WHERE ...): the pivot and the ordering happen in the database. Projected to
        // an anonymous type first, because EF cannot order by the members of a record it constructs.
        var modelRows = await query
            .Where(feedback => feedback.ModelId != null)
            .GroupBy(feedback => feedback.ModelId!)
            .Select(group => new
            {
                ModelId = group.Key,
                Positive = group.Count(feedback => feedback.Rating == AiFeedbackRating.Positive),
                Negative = group.Count(feedback => feedback.Rating == AiFeedbackRating.Negative),
            })
            .OrderByDescending(row => row.Negative)
            .ThenByDescending(row => row.Positive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byModel = modelRows
            .Select(row => new AiFeedbackModelCount(row.ModelId, row.Positive, row.Negative))
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
