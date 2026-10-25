using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.AI.Ingestion;

/// <summary>
/// Puts failed, unavailable and skipped sources back at the front of the queue, for an admin who knows
/// something has changed: an extractor fixed after a site redesign, a document shared again.
///
/// Skipping is meant to be permanent, so before this nothing could undo one — and when rmrg.me changed
/// its markup every one of its guides was skipped as "not a guide page", with no way back short of
/// editing the database.
/// </summary>
/// <param name="KeyOrUrl">One source, by its natural key or its link.</param>
/// <param name="SourceType">Every such source of one family; with a key, restricts the key to that family.</param>
public sealed record RetryKnowledgeSourcesCommand(string? KeyOrUrl = null, string? SourceType = null)
    : ICommand<Result<KnowledgeSourceRetryReport>>;

/// <param name="Matched">Sources that matched the description, whatever their state.</param>
/// <param name="Queued">How many of them were queued; the rest were indexed or already waiting.</param>
public sealed record KnowledgeSourceRetryReport(int Matched, int Queued);

public sealed partial class RetryKnowledgeSourcesHandler(
    IKnowledgeSourceRepository sources,
    ILogger<RetryKnowledgeSourcesHandler> logger)
    : IRequestHandler<RetryKnowledgeSourcesCommand, Result<KnowledgeSourceRetryReport>>
{
    public async Task<Result<KnowledgeSourceRetryReport>> Handle(
        RetryKnowledgeSourcesCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.KeyOrUrl) && string.IsNullOrWhiteSpace(request.SourceType))
        {
            // Every problem source of every family at once is a decision worth making family by family:
            // it would put a library's worth of dead links back ahead of everything that works.
            return Error.Validation("ai.source_retry_unscoped", "Name a source key or link, or a source type.");
        }

        var matched = await sources.ReadMatchingAsync(request.KeyOrUrl, request.SourceType, cancellationToken)
            .ConfigureAwait(false);

        // `/ai sources` shows each source as type:key, which is what an admin will paste. Tried second, so
        // a natural key that happens to contain a colon still matches as itself.
        if (matched.Count == 0 && TrySplitTypedKey(request.KeyOrUrl, request.SourceType, out var type, out var key))
        {
            matched = await sources.ReadMatchingAsync(key, type, cancellationToken).ConfigureAwait(false);
        }

        if (matched.Count == 0)
        {
            return Error.NotFound("ai.source_not_found", "No listed source matches that.");
        }

        var queued = 0;
        foreach (var source in matched)
        {
            if (source.RequestRetry())
            {
                queued++;
                LogQueued(logger, source.SourceType, source.NaturalKey);
            }
        }

        return new KnowledgeSourceRetryReport(matched.Count, queued);
    }

    /// <summary>
    /// Reads "gdoc:1Vcun…" as a source type and a key. A link is left alone: "https:" is no source type,
    /// though "gdoc:1Vcun…" is itself a well-formed URI, so the check is on the scheme, not on parsing.
    /// </summary>
    private static bool TrySplitTypedKey(string? keyOrUrl, string? sourceType, out string type, out string key)
    {
        type = key = string.Empty;

        var text = keyOrUrl?.Trim();
        if (string.IsNullOrEmpty(text) || IsWebLink(text))
        {
            return false;
        }

        var separator = text.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0 || separator == text.Length - 1)
        {
            return false;
        }

        type = text[..separator];
        key = text[(separator + 1)..];

        // A type named separately must agree with the one in the key; otherwise the admin meant something else.
        return string.IsNullOrWhiteSpace(sourceType) || type.Equals(sourceType.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWebLink(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var url)
        && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps);

    [LoggerMessage(LogLevel.Information, "Queued {SourceType}:{NaturalKey} again at an admin's request.")]
    static partial void LogQueued(ILogger logger, string sourceType, string naturalKey);
}
