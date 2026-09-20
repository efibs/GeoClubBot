using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.AI.Feedback;

/// <param name="Role">"user" or "assistant", spelled the way chat APIs and eval tooling expect.</param>
public sealed record AiFeedbackTurnRecord(
    int Ordinal,
    string Role,
    ulong AuthorDiscordUserId,
    string Content,
    IReadOnlyList<string> ImageUrls,
    string? ModelId,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// One rated conversation, flattened for export.
/// </summary>
/// <param name="RetrievedSourceUrls">
/// Every guide offered to the model, best match first. This is what separates "the right guide was
/// never retrieved" from "it was retrieved and the answer ignored it" — two failures that look
/// identical in the transcript and need completely different fixes.
/// </param>
/// <param name="CitedSourceUrls">
/// What the answer actually pointed at. Empty alongside a populated offer list is the uncited
/// fallback, where the guides were credited on the model's behalf rather than cited by it.
/// </param>
public sealed record AiFeedbackRecord(
    Guid FeedbackId,
    string Rating,
    string? Comment,
    ulong ReviewerDiscordUserId,
    ulong RatedDiscordMessageId,
    ulong ConversationId,
    ulong ChannelId,
    ulong? GuildId,
    string? ModelId,
    int AnswerDepth,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<AiFeedbackTurnRecord> Transcript,
    IReadOnlyList<string> RetrievedSourceUrls,
    IReadOnlyList<string> CitedSourceUrls);

/// <summary>
/// The archive as flat records, for analysis outside the bot.
/// </summary>
public sealed record ExportAiFeedbackQuery(
    AiFeedbackRating? Rating,
    int? Days,
    int? Limit) : IQuery<Result<IReadOnlyList<AiFeedbackRecord>>>;

public sealed class ExportAiFeedbackHandler(
    IAiFeedbackRepository feedbacks,
    IOptions<AiFeedbackConfiguration> configuration)
    : IRequestHandler<ExportAiFeedbackQuery, Result<IReadOnlyList<AiFeedbackRecord>>>
{
    public async Task<Result<IReadOnlyList<AiFeedbackRecord>>> Handle(
        ExportAiFeedbackQuery request,
        CancellationToken cancellationToken)
    {
        var maxRecords = Math.Max(1, configuration.Value.MaxExportRecords);
        var limit = request.Limit is > 0 ? Math.Min(request.Limit.Value, maxRecords) : maxRecords;

        DateTimeOffset? since = request.Days is > 0
            ? DateTimeOffset.UtcNow.AddDays(-request.Days.Value)
            : null;

        var entries = await feedbacks
            .ReadForExportAsync(request.Rating, since, limit, cancellationToken)
            .ConfigureAwait(false);

        return Result<IReadOnlyList<AiFeedbackRecord>>.Success([.. entries.Select(ToRecord)]);
    }

    private static AiFeedbackRecord ToRecord(AiAnswerFeedback feedback) =>
        new(
            feedback.FeedbackId,
            feedback.Rating.ToString().ToLowerInvariant(),
            feedback.Comment,
            feedback.ReviewerDiscordUserId,
            feedback.RatedDiscordMessageId,
            feedback.ConversationId,
            feedback.ChannelId,
            feedback.GuildId,
            feedback.ModelId,
            feedback.AnswerDepth,
            feedback.CreatedAtUtc,
            feedback.UpdatedAtUtc,
            [.. feedback.Turns.OrderBy(turn => turn.Ordinal).Select(ToTurnRecord)],
            RetrievedSourceUrlsOf(feedback),
            CitedSourceUrlsOf(feedback));

    private static AiFeedbackTurnRecord ToTurnRecord(AiFeedbackTurn turn) =>
        new(
            turn.Ordinal,
            turn.Role == AiTurnRole.Assistant ? "assistant" : "user",
            turn.AuthorDiscordUserId,
            turn.Content,
            turn.ImageUrls,
            turn.ModelId,
            turn.CreatedAtUtc);

    private static IReadOnlyList<string> RetrievedSourceUrlsOf(AiAnswerFeedback feedback) =>
        feedback.Turns.OrderBy(turn => turn.Ordinal).LastOrDefault()?.RetrievedSourceUrls ?? [];

    private static IReadOnlyList<string> CitedSourceUrlsOf(AiAnswerFeedback feedback) =>
        feedback.Turns.OrderBy(turn => turn.Ordinal).LastOrDefault()?.CitedSourceUrls ?? [];
}
