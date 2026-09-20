using Entities;

namespace UseCases.OutputPorts.Repositories;

/// <summary>
/// The permanent archive of rated conversations.
///
/// Named <c>...Repository</c> deliberately: the integration-test host only wires genuine EF
/// implementations for ports whose interface name ends in "Repository" — anything else under
/// <c>OutputPorts</c> is auto-substituted, which would leave the archive tests asserting against a
/// fake that stores nothing.
/// </summary>
public interface IAiFeedbackRepository
{
    /// <summary>
    /// The reviewer's existing verdict on one answer, tracked so it can be amended. This is the
    /// lookup behind the "second reaction corrects the first" rule.
    /// </summary>
    Task<AiAnswerFeedback?> ReadForUpdateAsync(
        ulong ratedDiscordMessageId,
        ulong reviewerDiscordUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// How many people have rated one answer. Drives the confirmation reaction, which marks the
    /// answer rather than any one reviewer.
    /// </summary>
    Task<int> CountForMessageAsync(ulong ratedDiscordMessageId, CancellationToken cancellationToken = default);

    /// <summary>Queues an archive entry; the unit of work commits it.</summary>
    void Add(AiAnswerFeedback feedback);

    /// <summary>Queues a removal; the transcript cascades with it.</summary>
    void Remove(AiAnswerFeedback feedback);

    /// <summary>Newest first, with transcripts loaded, for the JSONL export.</summary>
    Task<IReadOnlyList<AiAnswerFeedback>> ReadForExportAsync(
        AiFeedbackRating? rating,
        DateTimeOffset? sinceUtc,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Aggregate counts for the admin summary. Deliberately does not load transcripts.</summary>
    Task<AiFeedbackCounts> ReadSummaryAsync(
        DateTimeOffset? sinceUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes archive entries past retention. Returns how many were removed.</summary>
    Task<int> DeleteOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default);
}

/// <param name="ByModel">Verdicts per model id, so a model that started answering badly stands out.</param>
/// <param name="RecentComments">Newest written feedback, which is the part worth reading by eye.</param>
public sealed record AiFeedbackCounts(
    int Positive,
    int Negative,
    int WithComment,
    IReadOnlyList<AiFeedbackModelCount> ByModel,
    IReadOnlyList<AiFeedbackCommentPreview> RecentComments)
{
    public int Total => Positive + Negative;
}

public sealed record AiFeedbackModelCount(string ModelId, int Positive, int Negative);

public sealed record AiFeedbackCommentPreview(
    AiFeedbackRating Rating,
    string Comment,
    string? ModelId,
    DateTimeOffset CreatedAtUtc);
