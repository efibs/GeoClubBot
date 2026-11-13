using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Retrieval;
using Utilities;

namespace UseCases.UseCases.AI.KnowledgeGaps;

/// <summary>The questions the index answered badly over a period, grouped by country and topic.</summary>
/// <param name="Days">The period; null takes <c>AI:KnowledgeGaps:ReportDays</c>.</param>
public sealed record ReadKnowledgeGapReportQuery(int? Days = null) : IQuery<Result<KnowledgeGapReport>>;

/// <param name="Days">The period covered.</param>
/// <param name="WeakRetrievals">Weak retrievals recorded in it.</param>
/// <param name="Downrated">Answers rated 👎 in it.</param>
/// <param name="Groups">By country and topic, most questions first.</param>
public sealed record KnowledgeGapReport(int Days, int WeakRetrievals, int Downrated, IReadOnlyList<KnowledgeGapGroup> Groups);

/// <param name="Country">The country, as the index spells it; null for questions naming none.</param>
/// <param name="Topic">The topic; null for questions of no recognised topic.</param>
/// <param name="NoExcerpts">Questions the index offered nothing for.</param>
/// <param name="NothingFromTheCountry">Questions offered nothing from the country they name.</param>
/// <param name="NothingCited">Questions whose answer cited nothing it was offered.</param>
/// <param name="Downrated">Answers rated 👎.</param>
/// <param name="Examples">A few of the questions, newest first.</param>
public sealed record KnowledgeGapGroup(
    string? Country,
    string? Topic,
    int NoExcerpts,
    int NothingFromTheCountry,
    int NothingCited,
    int Downrated,
    IReadOnlyList<string> Examples)
{
    public int Total => NoExcerpts + NothingFromTheCountry + NothingCited + Downrated;
}

public sealed class ReadKnowledgeGapReportHandler(
    IAiRetrievalGapRepository gaps,
    IAiFeedbackRepository feedbacks,
    KnowledgeCountryDirectory countryDirectory,
    IOptions<AiKnowledgeGapsConfiguration> configuration,
    TimeProvider timeProvider)
    : IRequestHandler<ReadKnowledgeGapReportQuery, Result<KnowledgeGapReport>>
{
    /// <summary>Rated answers read at most; a month of a club's ratings is far fewer.</summary>
    private const int MaxRatedRead = 1_000;

    public async Task<Result<KnowledgeGapReport>> Handle(ReadKnowledgeGapReportQuery request, CancellationToken cancellationToken)
    {
        var settings = configuration.Value;
        var days = Math.Max(1, request.Days ?? settings.ReportDays);
        var since = timeProvider.GetUtcNow() - TimeSpan.FromDays(days);

        var recorded = await gaps.ReadSinceAsync(since, cancellationToken).ConfigureAwait(false);
        var rated = await feedbacks.ReadForExportAsync(AiFeedbackRating.Negative, since, MaxRatedRead, cancellationToken).ConfigureAwait(false);
        var gazetteer = await countryDirectory.ReadAsync(cancellationToken).ConfigureAwait(false);

        var entries = recorded
            .Select(gap => new Entry(gap.Country, gap.Topic, gap.Reason, gap.Question, gap.AskedAtUtc))
            .Concat(rated.Select(feedback =>
            {
                var question = feedback.Turns.Where(turn => turn.Role == AiTurnRole.User).OrderBy(turn => turn.Ordinal).LastOrDefault()?.Content ?? string.Empty;
                var subject = WeakRetrieval.Subject(question, gazetteer);
                return new Entry(subject.Country, subject.Topic, null, feedback.Comment is { Length: > 0 } comment ? $"{question} — 👎 \"{comment}\"" : question, feedback.CreatedAtUtc);
            }))
            .ToList();

        var groups = entries
            .GroupBy(entry => (Country: entry.Country?.ToLowerInvariant(), Topic: entry.Topic?.ToLowerInvariant()))
            .Select(group => new KnowledgeGapGroup(
                group.Key.Country,
                group.Key.Topic,
                group.Count(entry => entry.Reason == AiRetrievalGapReason.NoExcerpts),
                group.Count(entry => entry.Reason == AiRetrievalGapReason.NothingFromTheCountry),
                group.Count(entry => entry.Reason == AiRetrievalGapReason.NothingCited),
                group.Count(entry => entry.Reason is null),
                [.. group.OrderByDescending(entry => entry.At)
                    .Select(entry => entry.Question.Trim())
                    .Where(question => question.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(settings.ExamplesPerGroup)]))
            .OrderByDescending(group => group.Total)
            .ThenBy(group => group.Country is null)
            .ThenBy(group => group.Country, StringComparer.Ordinal)
            .Take(settings.MaxGroups)
            .ToList();

        return new KnowledgeGapReport(days, recorded.Count, rated.Count, groups);
    }

    private sealed record Entry(string? Country, string? Topic, AiRetrievalGapReason? Reason, string Question, DateTimeOffset At);
}
